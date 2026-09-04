using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public static class VariantMapper
{
    public static VariantResponse ToResponse(Variant v) => new()
    {
        Id = v.Id,
        Name = v.Name,
        Price = v.Price,
        Sku = v.SKU,
        Quantity = v.Quantity,
        IsActive = v.IsActive
    };
}
