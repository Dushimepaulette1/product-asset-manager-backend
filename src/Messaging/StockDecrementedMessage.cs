namespace ProductAssetManager.Api.Messaging;

public record StockDecrementedMessage(Guid VariantId, string Sku, int NewQuantity, Guid OrderId);
