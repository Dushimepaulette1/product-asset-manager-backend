using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _dbContext;

    public OrderService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var rowsUpdated = await _dbContext.Variants
                .Where(v => v.Id == variant.Id && v.Quantity >= request.Quantity)
                .ExecuteUpdateAsync(setters => setters.SetProperty(v => v.Quantity, v => v.Quantity - request.Quantity));

            if (rowsUpdated == 0)
            {
                var currentQuantity = await _dbContext.Variants
                    .Where(v => v.Id == variant.Id)
                    .Select(v => v.Quantity)
                    .FirstOrDefaultAsync();

                await transaction.RollbackAsync();
                return new CreateOrderResult(false, false, $"Only {currentQuantity} unit(s) of '{variant.Name}' are available.", null);
            }

            var order = new Order
            {
                UserId = userId,
                VariantId = variant.Id,
                QuantityPurchased = request.Quantity,
                UnitPriceAtPurchase = unitPrice,
                OrderDate = DateTime.UtcNow
            };

            _dbContext.Orders.Add(order);

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new OrderResponse
            {
                Id = order.Id,
                VariantId = variant.Id,
                VariantSku = variant.SKU,
                VariantName = variant.Name,
                QuantityPurchased = order.QuantityPurchased,
                UnitPriceAtPurchase = order.UnitPriceAtPurchase,
                TotalPrice = order.UnitPriceAtPurchase * order.QuantityPurchased,
                OrderDate = order.OrderDate
            };

            return new CreateOrderResult(true, false, null, response);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return new CreateOrderResult(false, false, "Could not complete the purchase due to a data conflict. Please try again.", null);
        }
    }
}
