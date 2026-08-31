namespace ProductAssetManager.Api.DTOs;

public record VariantResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal? Price { get; init; }

    public string Sku { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public bool IsActive { get; init; }
}
