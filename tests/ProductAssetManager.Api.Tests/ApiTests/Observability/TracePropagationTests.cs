using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

[TestFixture]
public class TracePropagationTests : ApiTestBase
{
    [Test]
    public async Task When_PurchaseIsProcessed_Should_PutTheHttpRequestAndTheConsumerSpanInOneTrace()
    {
        var variantId = await SeedVariantAsync("TRACE-PROPAGATION-SKU", 5);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());
        await ResumeConsumerAsync();

        var response = await Client.PostAsJsonAsync("/api/orders", new CreateOrderRequest { VariantId = variantId, Quantity = 1 });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>();
        var resolved = await PollUntilResolvedAsync(accepted!.OrderId, TimeSpan.FromSeconds(30));
        Assert.That(resolved.Status, Is.EqualTo(OrderStatus.Confirmed));

        var processSpan = await WaitForSpanAsync(activity =>
            activity.DisplayName == OrderTelemetry.ProcessSpanName
            && activity.GetTagItem("order.id") as string == accepted.OrderId.ToString());

        var purchaseRequestSpan = Factory.ExportedActivities.Snapshot().Single(activity =>
            activity.Kind == ActivityKind.Server && activity.DisplayName == "POST api/orders");

        Assert.That(processSpan, Is.Not.Null, "the consumer should record a processing span for the order");
        Assert.That(processSpan!.TraceId, Is.EqualTo(purchaseRequestSpan.TraceId));
    }

    private async Task<Activity?> WaitForSpanAsync(Func<Activity, bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var match = Factory.ExportedActivities.Snapshot().FirstOrDefault(predicate);

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(200);
        }

        return null;
    }
}
