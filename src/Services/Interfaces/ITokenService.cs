using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public interface ITokenService
{
    string GenerateToken(ApplicationUser user, IList<string> roles);
}
