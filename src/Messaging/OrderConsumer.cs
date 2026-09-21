using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Messaging;

public class OrderConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusClient _ordersClient;
    private readonly ServiceBusSender _stockEventsSender;
    private readonly string _ordersQueueName;
    private readonly bool _autoStart;
    private readonly ILogger<OrderConsumer> _logger;
    private ServiceBusSessionProcessor? _processor;

    public OrderConsumer(
        IServiceScopeFactory scopeFactory,
        ServiceBusClient ordersClient,
        ServiceBusSender stockEventsSender,
        IConfiguration configuration,
        ILogger<OrderConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _ordersClient = ordersClient;
        _stockEventsSender = stockEventsSender;
        _ordersQueueName = configuration["ServiceBus:QueueName"]
            ?? throw new InvalidOperationException("ServiceBus:QueueName is not configured.");
        _autoStart = configuration.GetValue("ServiceBus:AutoStartConsumer", true);
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
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var orderPlaced = args.Message.Body.ToObjectFromJson<OrderPlacedMessage>();

        if (orderPlaced is null)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        var order = await dbContext.Orders
            .Include(o => o.Variant)
            .FirstOrDefaultAsync(o => o.Id == orderPlaced.OrderId, args.CancellationToken);

        if (order is null)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        var variant = order.Variant;
        var confirmed = false;

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
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
                await dbContext.SaveChangesAsync(args.CancellationToken);
                break;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                foreach (var entry in ex.Entries)
                {
                    await entry.ReloadAsync(args.CancellationToken);
                }
            }
        }

        if (confirmed)
        {
            var stockEvent = new StockDecrementedMessage
            {
                VariantId = variant.Id,
                Sku = variant.SKU,
                NewQuantity = variant.Quantity,
                OrderId = order.Id
            };

            var stockMessage = new ServiceBusMessage(BinaryData.FromObjectAsJson(stockEvent))
            {
                MessageId = $"{order.Id}-stock-decremented"
            };

            await _stockEventsSender.SendMessageAsync(stockMessage, args.CancellationToken);
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus session processor error");
        return Task.CompletedTask;
    }
}
