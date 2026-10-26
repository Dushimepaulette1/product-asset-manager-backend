using System.Net;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Health;

[TestFixture]
public class VersionEndpointTests : ApiTestBase
{
    [Test]
    public async Task When_NoBuildInformationIsConfigured_Should_ReportDevAndUnknown()
    {
        var response = await Client.GetAsync("/version");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var version = await response.Content.ReadFromJsonAsync<VersionResponse>(JsonTestOptions.Default);
        Assert.That(version, Is.EqualTo(new VersionResponse("dev", "unknown")));
    }
}
