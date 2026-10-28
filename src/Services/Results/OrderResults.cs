using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public record CreateOrderResult(bool Succeeded, bool VariantNotFound, string? ValidationError, Guid? OrderId);

public record GetOrderResult(bool Succeeded, bool NotFound, bool Forbidden, OrderResponse? Order);

public record CancelOrderResult(bool Succeeded, bool NotFound, bool NotPending, bool ConcurrencyConflict, OrderStatus? CurrentStatus, OrderResponse? Order);
