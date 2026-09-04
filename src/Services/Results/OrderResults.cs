using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public record CreateOrderResult(bool Succeeded, bool VariantNotFound, string? ValidationError, OrderResponse? Order);
