using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public interface IVariantService
{
    Task<UpdateStockResult> UpdateStockAsync(string sku, int quantity);
}
