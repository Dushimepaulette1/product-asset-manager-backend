namespace ProductAssetManager.Api.DTOs;

public record LoginResponse
{
    public string Token { get; init; } = string.Empty;
}
