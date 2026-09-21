namespace ProductAssetManager.Api.Messaging;

public record StockDecrementedMessage
{
    public Guid VariantId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public int NewQuantity { get; init; }

    public Guid OrderId { get; init; }
}
