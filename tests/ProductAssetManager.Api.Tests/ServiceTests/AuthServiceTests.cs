using Microsoft.AspNetCore.Identity;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;

namespace ProductAssetManager.Api.Tests.ServiceTests;

[TestFixture]
public class AuthServiceTests : ServiceTestBase
{
    [Test]
    public async Task RegisterAsync_WithValidRequest_CreatesUserWithUserRole()
    {
        await RoleManager.CreateAsync(new IdentityRole(IdentitySeeder.UserRole));

        var authService = CreateAuthService();
        var request = new RegisterRequest
        {
            Name = "Placeholder Test User",
            Email = "placeholder.test@example.com",
            Password = "Passw0rd!"
        };

        var result = await authService.RegisterAsync(request);

        Assert.That(result.Succeeded, Is.True);

        var createdUser = await UserManager.FindByEmailAsync(request.Email);
        Assert.That(createdUser, Is.Not.Null);

        var roles = await UserManager.GetRolesAsync(createdUser!);
        Assert.That(roles, Does.Contain(IdentitySeeder.UserRole));
    }
}
