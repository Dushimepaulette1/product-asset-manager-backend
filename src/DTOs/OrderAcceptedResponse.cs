namespace ProductAssetManager.Api.DTOs;

public record OrderAcceptedResponse
{
    public Guid OrderId { get; init; }
}
