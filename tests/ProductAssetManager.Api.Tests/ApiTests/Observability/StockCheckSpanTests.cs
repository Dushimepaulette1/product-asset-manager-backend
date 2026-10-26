using System.Diagnostics;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

[TestFixture]
public class StockCheckSpanTests : ApiTestBase
{
    private const string Sku = "STOCK-CHECK-SPAN-SKU";

    [SetUp]
    public Task SignInAndStartConsumer() => SignInAsShopperAndStartConsumerAsync();

    [Test]
    public async Task When_OrderIsConfirmed_Should_RecordTheStockCheckWithItsOutcomeAroundTheSave()
    {
        var variantId = await SeedVariantAsync(Sku, 5);

        var orderId = await PlaceOrderAndWaitForStatusAsync(variantId, 2, OrderStatus.Confirmed);

        var processSpan = await WaitForOrderSpanAsync(OrderTelemetry.ProcessSpanName, orderId);
        var stockCheckSpan = await WaitForOrderSpanAsync(OrderTelemetry.StockCheckSpanName, orderId);

        Assert.That(processSpan, Is.Not.Null);
        Assert.That(stockCheckSpan, Is.Not.Null);
        Assert.That(stockCheckSpan!.ParentSpanId, Is.EqualTo(processSpan!.SpanId), "the stock check runs inside the processing span");
        Assert.That(stockCheckSpan.GetTagItem("variant.sku"), Is.EqualTo(Sku));
        Assert.That(stockCheckSpan.GetTagItem("order.outcome"), Is.EqualTo(OrderTelemetry.OutcomeConfirmed));
        Assert.That(stockCheckSpan.GetTagItem("order.rejection_reason"), Is.Null);

        var databaseSpansInsideStockCheck = Factory.ExportedActivities.Snapshot()
            .Where(span => span.ParentSpanId == stockCheckSpan.SpanId && span.Kind == ActivityKind.Client)
            .ToList();
        Assert.That(databaseSpansInsideStockCheck, Is.Not.Empty, "the decrement's SaveChanges is a database span inside the stock check");
    }

    [Test]
    public async Task When_OrderIsRejected_Should_RecordTheOutcomeAndTheReason()
    {
        var variantId = await SeedVariantAsync(Sku, 1);

        var orderId = await PlaceOrderAndWaitForStatusAsync(variantId, 3, OrderStatus.Rejected);

        var stockCheckSpan = await WaitForOrderSpanAsync(OrderTelemetry.StockCheckSpanName, orderId);

        Assert.That(stockCheckSpan, Is.Not.Null);
        Assert.That(stockCheckSpan!.GetTagItem("order.outcome"), Is.EqualTo(OrderTelemetry.OutcomeRejected));
        Assert.That(stockCheckSpan.GetTagItem("order.rejection_reason"), Is.EqualTo(OrderTelemetry.RejectionReasonInsufficientStock));
    }
}
