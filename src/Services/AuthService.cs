using Microsoft.AspNetCore.Identity;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;

    public AuthService(UserManager<ApplicationUser> userManager, ITokenService tokenService)
    {
        _userManager = userManager;
        _tokenService = tokenService;
    }

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.Name
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);

        if (!createResult.Succeeded)
        {
            return new RegisterResult(false, null, null, createResult.Errors.Select(e => e.Description));
        }

        var roleResult = await _userManager.AddToRoleAsync(user, IdentitySeeder.UserRole);

        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            return new RegisterResult(false, null, null, roleResult.Errors.Select(e => e.Description));
        }

        return new RegisterResult(true, user.Id, user.Email, Enumerable.Empty<string>());
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user is null)
        {
            return new LoginResult(false, null);
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);

        if (!passwordValid)
        {
            return new LoginResult(false, null);
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.GenerateToken(user, roles);

        return new LoginResult(true, token);
    }
}
