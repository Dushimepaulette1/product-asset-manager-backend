using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Services;

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request);

    Task<LoginResult> LoginAsync(LoginRequest request);
}
