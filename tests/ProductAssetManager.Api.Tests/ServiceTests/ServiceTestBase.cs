using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Services;

namespace ProductAssetManager.Api.Tests.ServiceTests;

public abstract class ServiceTestBase
{
    protected ApplicationDbContext DbContext { get; private set; } = null!;
    protected UserManager<ApplicationUser> UserManager { get; private set; } = null!;
    protected RoleManager<IdentityRole> RoleManager { get; private set; } = null!;
    protected ITokenService TokenService { get; private set; } = null!;

    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task ServiceTestBaseSetUp()
    {
        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(TestDatabase.ServiceTestsConnectionString));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        var configValues = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["Jwt:SigningKey"] = "this-is-a-test-only-signing-key-not-used-anywhere-real",
            ["Jwt:ExpiryMinutes"] = "60"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddLogging();

        _provider = services.BuildServiceProvider();

        await TestDatabase.ResetAsync(TestDatabase.ServiceTestsConnectionString);
        DbContext = _provider.GetRequiredService<ApplicationDbContext>();

        UserManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager = _provider.GetRequiredService<RoleManager<IdentityRole>>();
        TokenService = _provider.GetRequiredService<ITokenService>();
    }

    [TearDown]
    public async Task ServiceTestBaseTearDown()
    {
        await DbContext.Database.EnsureDeletedAsync();
        UserManager.Dispose();
        RoleManager.Dispose();
        await DbContext.DisposeAsync();
        await _provider.DisposeAsync();
    }

    protected IAuthService CreateAuthService() => _provider.GetRequiredService<IAuthService>();
}
