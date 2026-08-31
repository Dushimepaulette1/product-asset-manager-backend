using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public interface IProductService
{
    Task<CreateProductResult> CreateAsync(CreateProductRequest request);
}
