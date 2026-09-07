namespace ProductAssetManager.Api.DTOs;

public record CreateOrderRequest
{
    public Guid VariantId { get; init; }

    public int Quantity { get; init; }
}
