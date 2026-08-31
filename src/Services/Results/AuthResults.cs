namespace ProductAssetManager.Api.Services;

public record RegisterResult(bool Succeeded, string? UserId, string? Email, IEnumerable<string> Errors);

public record LoginResult(bool Succeeded, string? Token);
