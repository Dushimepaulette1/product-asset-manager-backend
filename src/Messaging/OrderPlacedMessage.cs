namespace ProductAssetManager.Api.Messaging;

public record OrderPlacedMessage
{
    public Guid OrderId { get; init; }
}
