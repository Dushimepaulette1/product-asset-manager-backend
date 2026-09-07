namespace ProductAssetManager.Api.DTOs;

public record OrderResponse
{
    public Guid Id { get; init; }

    public Guid VariantId { get; init; }

    public string VariantSku { get; init; } = string.Empty;

    public string VariantName { get; init; } = string.Empty;

    public int QuantityPurchased { get; init; }

    public decimal UnitPriceAtPurchase { get; init; }

    public decimal TotalPrice { get; init; }

    public DateTime OrderDate { get; init; }
}
