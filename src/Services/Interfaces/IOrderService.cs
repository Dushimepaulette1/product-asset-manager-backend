using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public interface IOrderService
{
    Task<CreateOrderResult> CreateAsync(string userId, CreateOrderRequest request);
}
