namespace ProductAssetManager.Api.DTOs;

public record ProductResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal BasePrice { get; init; }

    public string Material { get; init; } = string.Empty;

    public Guid CategoryId { get; init; }

    public string? CategoryName { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }

    public List<VariantResponse> Variants { get; init; } = new();
}
