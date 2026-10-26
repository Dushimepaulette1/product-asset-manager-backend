using System.Diagnostics;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

[TestFixture]
public class TraceSamplingTests : ApiTestBase
{
    [Test]
    public async Task When_SamplingRatioIsNotSet_Should_KeepEveryTrace()
    {
        await Client.GetAsync($"/api/products/{Guid.NewGuid()}");

        var span = await WaitForSpanAsync(IsProductRequestSpan);

        Assert.That(span, Is.Not.Null);
    }

    [Test]
    public async Task When_SamplingRatioIsZero_Should_KeepNoTraces()
    {
        Client.Dispose();
        await Factory.DisposeAsync();

        await using var unsampledFactory = new CustomWebApplicationFactory(new Dictionary<string, string?>
        {
            ["OpenTelemetry:TraceSamplingRatio"] = "0"
        });
        using var unsampledClient = unsampledFactory.CreateClient();

        await unsampledClient.GetAsync($"/api/products/{Guid.NewGuid()}");
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.That(unsampledFactory.ExportedActivities.Snapshot().Where(IsProductRequestSpan), Is.Empty);
    }

    private static bool IsProductRequestSpan(Activity span) =>
        span.Kind == ActivityKind.Server && span.DisplayName == "GET api/products/{id:guid}";
}
