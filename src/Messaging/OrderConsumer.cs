using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;

namespace ProductAssetManager.Api.Messaging;

public class OrderConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusClient _ordersClient;
    private readonly ServiceBusSender _stockEventsSender;
    private readonly string _ordersQueueName;
    private readonly bool _autoStart;
    private readonly OrderMetrics _orderMetrics;
    private readonly ILogger<OrderConsumer> _logger;
    private ServiceBusSessionProcessor? _processor;

    public OrderConsumer(
        IServiceScopeFactory scopeFactory,
        ServiceBusClient ordersClient,
        ServiceBusSender stockEventsSender,
        IConfiguration configuration,
        OrderMetrics orderMetrics,
        ILogger<OrderConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _ordersClient = ordersClient;
        _stockEventsSender = stockEventsSender;
        _ordersQueueName = configuration["ServiceBus:QueueName"]
            ?? throw new InvalidOperationException("ServiceBus:QueueName is not configured.");
        _autoStart = configuration.GetValue("ServiceBus:AutoStartConsumer", true);
        _orderMetrics = orderMetrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _ordersClient.CreateSessionProcessor(_ordersQueueName, new ServiceBusSessionProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentSessions = 5,
            MaxConcurrentCallsPerSession = 1
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        if (_autoStart)
        {
            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Order consumer is listening on queue {QueueName}", _ordersQueueName);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task PauseProcessingAsync(CancellationToken cancellationToken = default)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }
    }

    public async Task ResumeProcessingAsync(CancellationToken cancellationToken = default)
    {
        if (_processor is not null)
        {
            await _processor.StartProcessingAsync(cancellationToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task ProcessMessageAsync(ProcessSessionMessageEventArgs args)
    {
        var parentContext = MessageTraceContext.Extract(args.Message);

        using var processActivity = OrderTelemetry.ActivitySource.StartActivity(
            OrderTelemetry.ProcessSpanName,
            ActivityKind.Consumer,
            parentContext.ActivityContext);
        processActivity?.SetTag("messaging.system", "servicebus");
        processActivity?.SetTag("messaging.destination.name", _ordersQueueName);
        processActivity?.SetTag("messaging.message.id", args.Message.MessageId);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var orderPlaced = ReadOrderPlaced(args.Message);

        if (orderPlaced is null)
        {
            _logger.LogError(
                "Message {MessageId} has an unusable body and was dead-lettered",
                args.Message.MessageId);

            await args.DeadLetterMessageAsync(
                args.Message,
                "InvalidMessageBody",
                "The body is not a valid OrderPlaced message carrying an order id.",
                args.CancellationToken);
            return;
        }

        processActivity?.SetTag("order.id", orderPlaced.OrderId.ToString());

        var order = await dbContext.Orders
            .Include(o => o.Variant)
            .FirstOrDefaultAsync(o => o.Id == orderPlaced.OrderId, args.CancellationToken);

        if (order is null)
        {
            _logger.LogWarning(
                "Order {OrderId} from message {MessageId} was not found; completing the message because retrying cannot help",
                orderPlaced.OrderId,
                args.Message.MessageId);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            await SkipNonPendingAsync(args, order, processActivity);
            return;
        }

        var variant = order.Variant;
        var confirmed = false;
        var saved = false;
        var orderDeleted = false;
        string? outcome = null;
        string? rejectionReason = null;

        using (var stockCheckActivity = OrderTelemetry.ActivitySource.StartActivity(OrderTelemetry.StockCheckSpanName))
        {
            stockCheckActivity?.SetTag("order.id", order.Id.ToString());
            stockCheckActivity?.SetTag("variant.sku", variant.SKU);
            stockCheckActivity?.SetTag("order.quantity", order.QuantityPurchased);

            var saveAttempts = 0;

            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (order.Status != OrderStatus.Pending)
                {
                    break;
                }

                if (variant.Quantity >= order.QuantityPurchased)
                {
                    variant.Quantity -= order.QuantityPurchased;
                    order.Status = OrderStatus.Confirmed;
                    order.RejectionReason = null;
                    confirmed = true;
                }
                else
                {
                    order.Status = OrderStatus.Rejected;
                    order.RejectionReason = $"Only {variant.Quantity} unit(s) of '{variant.Name}' were available.";
                    confirmed = false;
                }

                try
                {
                    saveAttempts++;
                    await dbContext.SaveChangesAsync(args.CancellationToken);
                    saved = true;
                    break;
                }
                catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        "Concurrency conflict saving order {OrderId} on attempt {Attempt} ({ConflictingEntities}); reloading the order and its variant",
                        order.Id,
                        attempt,
                        string.Join(", ", ex.Entries.Select(entry => entry.Metadata.ClrType.Name)));

                    // The failed save rolled back both updates, so reload both: reloading only the conflicting
                    // entry would keep the other's in-memory change, such as a stock decrement that would apply twice.
                    await dbContext.Entry(order).ReloadAsync(args.CancellationToken);
                    await dbContext.Entry(variant).ReloadAsync(args.CancellationToken);

                    if (dbContext.Entry(order).State == EntityState.Detached)
                    {
                        orderDeleted = true;
                        break;
                    }
                }
            }

            if (saved)
            {
                outcome = confirmed ? OrderTelemetry.OutcomeConfirmed : OrderTelemetry.OutcomeRejected;
                rejectionReason = confirmed ? null : OrderTelemetry.RejectionReasonInsufficientStock;
            }
            else if (order.Status == OrderStatus.Cancelled)
            {
                outcome = OrderTelemetry.OutcomeSkippedCancelled;
            }

            stockCheckActivity?.SetTag("order.outcome", outcome);
            stockCheckActivity?.SetTag("order.rejection_reason", rejectionReason);
            stockCheckActivity?.SetTag("order.save_attempts", saveAttempts);
        }

        if (orderDeleted)
        {
            _logger.LogWarning(
                "Order {OrderId} from message {MessageId} was deleted while it was being processed; completing the message because retrying cannot help",
                order.Id,
                args.Message.MessageId);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        if (!saved)
        {
            await SkipNonPendingAsync(args, order, processActivity);
            return;
        }

        _orderMetrics.OrderProcessed(outcome!, rejectionReason, DateTime.UtcNow - order.OrderDate);

        if (confirmed)
        {
            _logger.LogInformation(
                "Order {OrderId} confirmed for SKU {Sku}, {Remaining} unit(s) remaining",
                order.Id,
                variant.SKU,
                variant.Quantity);

            var stockEvent = new StockDecrementedMessage(
                VariantId: variant.Id,
                Sku: variant.SKU,
                NewQuantity: variant.Quantity,
                OrderId: order.Id);

            var stockMessage = new ServiceBusMessage(BinaryData.FromObjectAsJson(stockEvent))
            {
                MessageId = $"{order.Id}-stock-decremented"
            };

            await _stockEventsSender.SendMessageAsync(stockMessage, args.CancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Order {OrderId} rejected for SKU {Sku}: {Reason}",
                order.Id,
                variant.SKU,
                order.RejectionReason);
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private async Task SkipNonPendingAsync(ProcessSessionMessageEventArgs args, Order order, Activity? processActivity)
    {
        if (order.Status == OrderStatus.Cancelled)
        {
            processActivity?.SetTag("order.outcome", OrderTelemetry.OutcomeSkippedCancelled);

            _logger.LogInformation(
                "Order {OrderId} was cancelled before processing; skipping without changing stock",
                order.Id);
        }
        else
        {
            _logger.LogInformation(
                "Order {OrderId} is already {Status}; ignoring a redelivered message",
                order.Id,
                order.Status);
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private static OrderPlacedMessage? ReadOrderPlaced(ServiceBusReceivedMessage message)
    {
        try
        {
            var orderPlaced = message.Body.ToObjectFromJson<OrderPlacedMessage>();
            return orderPlaced is null || orderPlaced.OrderId == Guid.Empty ? null : orderPlaced;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus session processor error");
        return Task.CompletedTask;
    }
}
