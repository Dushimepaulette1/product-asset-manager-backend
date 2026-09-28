using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.DTOs;

public record OrderResponse(
    Guid Id,
    OrderStatus Status,
    string? RejectionReason,
    Guid VariantId,
    string VariantSku,
    string VariantName,
    int QuantityPurchased,
    decimal UnitPriceAtPurchase,
    decimal TotalPrice,
    DateTime OrderDate);
