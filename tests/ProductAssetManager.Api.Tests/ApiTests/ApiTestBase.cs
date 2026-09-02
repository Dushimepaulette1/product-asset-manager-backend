using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Services;

namespace ProductAssetManager.Api.Tests.ApiTests;

public abstract class ApiTestBase
{
    protected CustomWebApplicationFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    [SetUp]
    public async Task ApiTestBaseSetUp()
    {
        await TestDatabase.ResetAsync(TestDatabase.ApiTestsConnectionString);
        Factory = new CustomWebApplicationFactory();
        Client = Factory.CreateClient();
    }

    [TearDown]
    public async Task ApiTestBaseTearDown()
    {
        Client.Dispose();
        await Factory.DisposeAsync();

        await TestDatabase.DeleteAsync(TestDatabase.ApiTestsConnectionString);
    }

    protected async Task<string> CreateAdminTokenAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        if (!await roleManager.RoleExistsAsync(IdentitySeeder.AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(IdentitySeeder.AdminRole));
        }

        var email = $"admin-test-{Guid.NewGuid()}@example.com";
        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Test Admin"
        };

        await userManager.CreateAsync(admin, "AdminPassw0rd!");
        await userManager.AddToRoleAsync(admin, IdentitySeeder.AdminRole);

        return tokenService.GenerateToken(admin, new List<string> { IdentitySeeder.AdminRole });
    }
}
