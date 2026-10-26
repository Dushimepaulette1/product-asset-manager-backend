using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public record CreateOrderResult(bool Succeeded, bool VariantNotFound, string? ValidationError, Guid? OrderId);

public record GetOrderResult(bool Succeeded, bool NotFound, bool Forbidden, OrderResponse? Order);
