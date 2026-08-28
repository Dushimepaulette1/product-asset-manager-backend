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
}
