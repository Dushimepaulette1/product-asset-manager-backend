using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public static class OrderMapper
{
    public static OrderResponse ToResponse(Order o) => new(
        Id: o.Id,
        Status: o.Status,
        RejectionReason: o.RejectionReason,
        VariantId: o.VariantId,
        VariantSku: o.Variant.SKU,
        VariantName: o.Variant.Name,
        QuantityPurchased: o.QuantityPurchased,
        UnitPriceAtPurchase: o.UnitPriceAtPurchase,
        TotalPrice: o.UnitPriceAtPurchase * o.QuantityPurchased,
        OrderDate: o.OrderDate);
}
