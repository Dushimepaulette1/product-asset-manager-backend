using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public static class ProductMapper
{
    public static PublicProductResponse ToPublicResponse(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        BasePrice = p.BasePrice,
        Material = p.Material,
        CategoryId = p.CategoryId,
        CategoryName = p.Category.Name,
        Variants = p.Variants
            .Where(v => v.IsActive)
            .Select(v => new PublicVariantResponse
            {
                Id = v.Id,
                Name = v.Name,
                Price = v.Price,
                Sku = v.SKU,
                StockStatus = GetStockStatus(v.Quantity)
            })
            .ToList()
    };

    public static string GetStockStatus(int quantity)
    {
        if (quantity == 0)
        {
            return "OUT_OF_STOCK";
        }

        return quantity < 5 ? "LOW_STOCK" : "IN_STOCK";
    }
}
