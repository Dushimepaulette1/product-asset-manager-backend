using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public record AddProductToCollectionResult(bool Succeeded, bool CollectionNotFound, bool ProductNotFound, bool AlreadyMember, CollectionResponse? Collection);

public record RemoveProductFromCollectionResult(bool Succeeded, bool CollectionNotFound, bool ProductNotFound);
