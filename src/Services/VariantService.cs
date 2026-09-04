using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Data;

namespace ProductAssetManager.Api.Services;

public class VariantService : IVariantService
{
    private readonly ApplicationDbContext _dbContext;

    public VariantService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UpdateStockResult> UpdateStockAsync(string sku, int quantity)
    {
        if (quantity < 0)
        {
            return new UpdateStockResult(false, false, "Quantity must be zero or greater.", null);
        }

        var variant = await _dbContext.Variants.FirstOrDefaultAsync(v => v.SKU == sku);

        if (variant is null)
        {
            return new UpdateStockResult(false, true, null, null);
        }

        variant.Quantity = quantity;

        await _dbContext.SaveChangesAsync();

        return new UpdateStockResult(true, false, null, VariantMapper.ToResponse(variant));
    }
}
