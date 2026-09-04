namespace ProductAssetManager.Api.DTOs;

public record AddProductToCollectionRequest
{
    public Guid ProductId { get; init; }
}
