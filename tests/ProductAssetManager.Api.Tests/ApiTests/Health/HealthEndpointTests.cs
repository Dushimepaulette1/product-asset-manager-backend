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

    [Test]
    public async Task When_GettingLiveEndpoint_Should_ReturnHealthy()
    {
        var response = await Client.GetAsync("/health/live");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
    }

    [Test]
    public async Task When_DatabaseAndServiceBusAreReachable_Should_ReportReady()
    {
        var response = await Client.GetAsync("/health/ready");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
    }
}
