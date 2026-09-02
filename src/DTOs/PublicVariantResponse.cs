namespace ProductAssetManager.Api.DTOs;

public record PublicVariantResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal? Price { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string StockStatus { get; init; } = string.Empty;
}
