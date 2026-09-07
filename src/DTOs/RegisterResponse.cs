namespace ProductAssetManager.Api.DTOs;

public record RegisterResponse
{
    public string Id { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;
}
