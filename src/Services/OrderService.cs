using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Messaging;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ServiceBusSender _ordersSender;

    public OrderService(
        ApplicationDbContext dbContext,
        [FromKeyedServices(ServiceBusQueues.Orders)] ServiceBusSender ordersSender)
    {
        _dbContext = dbContext;
        _ordersSender = ordersSender;
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
            await _ordersSender.SendMessageAsync(message);
        }
        catch
        {
            _dbContext.Orders.Remove(order);
            await _dbContext.SaveChangesAsync();
            throw;
        }

        return new CreateOrderResult(true, false, null, order.Id);
    }
}
