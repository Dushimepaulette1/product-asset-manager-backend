using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public interface IProductService
{
    Task<CreateProductResult> CreateAsync(CreateProductRequest request);

    Task<List<PublicProductResponse>> SearchAsync(string? keyword, decimal? maxPrice);

    Task<PublicProductResponse?> GetByIdAsync(Guid id);

    Task<AddVariantResult> AddVariantAsync(Guid productId, CreateVariantRequest request);
}
