using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Data;

namespace ProductAssetManager.Api.Tests;

public static class TestDatabase
{
    public const string ServiceTestsConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=ProductAssetManagerServiceTestsDb;Trusted_Connection=True;MultipleActiveResultSets=true";

    public const string ApiTestsConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=ProductAssetManagerApiTestsDb;Trusted_Connection=True;MultipleActiveResultSets=true";

    public static async Task ResetAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
    }
}
