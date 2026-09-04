using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public interface ICollectionService
{
    Task<CollectionResponse> CreateAsync(CreateCollectionRequest request);

    Task<CollectionResponse?> GetByIdAsync(Guid id);

    Task<AddProductToCollectionResult> AddProductAsync(Guid collectionId, Guid productId);

    Task<RemoveProductFromCollectionResult> RemoveProductAsync(Guid collectionId, Guid productId);
}
