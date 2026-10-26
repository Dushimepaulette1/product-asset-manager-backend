using Microsoft.EntityFrameworkCore;

namespace ProductAssetManager.Api.Data;

public static class DatabaseInitializer
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        logger.LogInformation("Applying database migrations");
        await dbContext.Database.MigrateAsync();

        logger.LogInformation("Seeding roles and the admin account");
        await IdentitySeeder.SeedAsync(scope.ServiceProvider, configuration);

        if (configuration.GetValue("Seed:DemoData", false))
        {
            logger.LogInformation("Seeding the demo catalogue");
            await DemoCatalogSeeder.SeedAsync(dbContext);
        }

        logger.LogInformation("Database is migrated and seeded");
    }
}
