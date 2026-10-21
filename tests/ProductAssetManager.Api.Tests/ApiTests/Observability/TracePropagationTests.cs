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
        await SignInAsShopperAndStartConsumerAsync();

        var orderId = await PlaceOrderAndWaitForStatusAsync(variantId, 1, OrderStatus.Confirmed);

        var processSpan = await WaitForOrderSpanAsync(OrderTelemetry.ProcessSpanName, orderId);

        Assert.That(processSpan, Is.Not.Null, "the consumer should record a processing span for the order");
        Assert.That(processSpan!.TraceId, Is.EqualTo(PurchaseRequestSpan().TraceId));
    }
}
