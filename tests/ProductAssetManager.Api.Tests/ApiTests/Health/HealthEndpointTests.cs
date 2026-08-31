using System.Net;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Health;

[TestFixture]
public class HealthEndpointTests : ApiTestBase
{
    [Test]
    public async Task When_GettingHealthEndpoint_Should_ReturnOkWithStatusOk()
    {
        var response = await Client.GetAsync("/api/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("\"status\":\"ok\""));
    }
}
