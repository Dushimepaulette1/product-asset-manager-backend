using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

[TestFixture]
public class StructuredLogTests : ApiTestBase
{
    private const string ConfirmedTemplate = "Order {OrderId} confirmed for SKU {Sku}, {Remaining} unit(s) remaining";

    [Test]
    public async Task When_ConsumerLogsAConfirmedOrder_Should_KeepTheTemplateAndCarryTheTraceAndSpanIds()
    {
        var variantId = await SeedVariantAsync("STRUCTURED-LOG-SKU", 5);
        await SignInAsShopperAndStartConsumerAsync();

        var orderId = await PlaceOrderAndWaitForStatusAsync(variantId, 1, OrderStatus.Confirmed);

        var processSpan = await WaitForOrderSpanAsync(OrderTelemetry.ProcessSpanName, orderId);
        var confirmedLog = Factory.CapturedLogs.Snapshot().SingleOrDefault(log =>
            log.Body == ConfirmedTemplate && Equals(log.Attributes.GetValueOrDefault("OrderId"), orderId));

        Assert.That(confirmedLog, Is.Not.Null, "the consumer should log the confirmation with its message template");
        Assert.That(confirmedLog!.TraceId, Is.EqualTo(PurchaseRequestSpan().TraceId), "the log belongs to the purchase's trace");
        Assert.That(confirmedLog.SpanId, Is.EqualTo(processSpan!.SpanId), "the log was written inside the processing span");
        Assert.That(confirmedLog.Attributes["Sku"], Is.EqualTo("STRUCTURED-LOG-SKU"));
    }
}
