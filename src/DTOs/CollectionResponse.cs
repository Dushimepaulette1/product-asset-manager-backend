namespace ProductAssetManager.Api.DTOs;

public record CollectionResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public List<PublicProductResponse> Products { get; init; } = new();
}
