using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Data;

namespace ProductAssetManager.Api.Tests;

public static class TestDatabase
{
    public const string ServerConnectionStringVariable = "TEST_SQL_CONNECTION_STRING";

    private const string LocalDbServerConnectionString =
        "Server=(localdb)\\mssqllocaldb;Trusted_Connection=True;MultipleActiveResultSets=true";

    public static readonly string ServiceTestsConnectionString = BuildConnectionString("ProductAssetManagerServiceTestsDb");

    public static readonly string ApiTestsConnectionString = BuildConnectionString("ProductAssetManagerApiTestsDb");

    private static string BuildConnectionString(string databaseName)
    {
        var serverConnectionString = Environment.GetEnvironmentVariable(ServerConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            serverConnectionString = LocalDbServerConnectionString;
        }

        return new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;
    }

    public static async Task ResetAsync(string connectionString)
    {
        await DeleteAsync(connectionString);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public static async Task DeleteAsync(string connectionString)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            SqlConnection.ClearPool(connection);
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureDeletedAsync();
    }
}
