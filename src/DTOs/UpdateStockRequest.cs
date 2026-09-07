namespace ProductAssetManager.Api.DTOs;

public record UpdateStockRequest
{
    public int Quantity { get; init; }
}
