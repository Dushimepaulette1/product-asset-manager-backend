using Microsoft.AspNetCore.Identity;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ServiceTests;

namespace ProductAssetManager.Api.Tests.ServiceTests.Auth;

[TestFixture]
public class AuthServiceTests : ServiceTestBase
{
    [Test]
    public async Task When_RegisteringWithValidRequest_Should_CreateUserWithUserRole()
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
