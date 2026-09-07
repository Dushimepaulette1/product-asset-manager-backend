namespace ProductAssetManager.Api.DTOs;

public record PublicProductResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal BasePrice { get; init; }

    public string Material { get; init; } = string.Empty;

    public Guid CategoryId { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public List<PublicVariantResponse> Variants { get; init; } = new();
}
