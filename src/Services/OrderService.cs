using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Messaging;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;

namespace ProductAssetManager.Api.Services;

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ServiceBusSender _ordersSender;
    private readonly OrderMetrics _orderMetrics;

    public OrderService(
        ApplicationDbContext dbContext,
        [FromKeyedServices(ServiceBusQueues.Orders)] ServiceBusSender ordersSender,
        OrderMetrics orderMetrics)
    {
        _dbContext = dbContext;
        _ordersSender = ordersSender;
        _orderMetrics = orderMetrics;
    }

    public async Task<CreateOrderResult> CreateAsync(string userId, CreateOrderRequest request)
    {
        if (request.Quantity < 1)
        {
            return new CreateOrderResult(false, false, "Quantity must be at least 1.", null);
        }

        var variant = await _dbContext.Variants
            .Include(v => v.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.VariantId);

        if (variant is null || !variant.IsActive)
        {
            return new CreateOrderResult(false, true, null, null);
        }

        var unitPrice = variant.Price ?? variant.Product.BasePrice;

        var order = new Order
        {
            UserId = userId,
            VariantId = variant.Id,
            QuantityPurchased = request.Quantity,
            UnitPriceAtPurchase = unitPrice,
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Pending
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(new OrderPlacedMessage { OrderId = order.Id }))
        {
            MessageId = order.Id.ToString(),
            SessionId = variant.SKU
        };

        try
        {
            using var publishActivity = OrderTelemetry.ActivitySource.StartActivity(OrderTelemetry.PublishSpanName, ActivityKind.Producer);
            publishActivity?.SetTag("messaging.system", "servicebus");
            publishActivity?.SetTag("messaging.destination.name", _ordersSender.EntityPath);
            publishActivity?.SetTag("messaging.message.id", message.MessageId);
            publishActivity?.SetTag("order.id", order.Id.ToString());

            MessageTraceContext.Inject(publishActivity ?? Activity.Current, message);

            await _ordersSender.SendMessageAsync(message);
        }
        catch
        {
            _dbContext.Orders.Remove(order);
            await _dbContext.SaveChangesAsync();
            throw;
        }

        _orderMetrics.OrderPlaced();

        return new CreateOrderResult(true, false, null, order.Id);
    }

    public async Task<GetOrderResult> GetByIdAsync(Guid orderId, string requestingUserId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Variant)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return new GetOrderResult(false, true, false, null);
        }

        if (!isAdmin && order.UserId != requestingUserId)
        {
            return new GetOrderResult(false, false, true, null);
        }

        return new GetOrderResult(true, false, false, OrderMapper.ToResponse(order));
    }
}
