using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Observability;

[TestFixture]
public class StockCheckSpanTests : ApiTestBase
{
    private const string Sku = "STOCK-CHECK-SPAN-SKU";

    [SetUp]
    public async Task AuthorizeAsUserAndStartConsumer()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());
        await ResumeConsumerAsync();
    }

    [Test]
    public async Task When_OrderIsConfirmed_Should_RecordTheStockCheckWithItsOutcomeAroundTheSave()
    {
        var variantId = await SeedVariantAsync(Sku, 5);

        var orderId = await PlaceOrderAndWaitAsync(variantId, 2, OrderStatus.Confirmed);

        var processSpan = await WaitForSpanAsync(span => IsSpanFor(span, OrderTelemetry.ProcessSpanName, orderId));
        var stockCheckSpan = await WaitForSpanAsync(span => IsSpanFor(span, OrderTelemetry.StockCheckSpanName, orderId));

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

        var orderId = await PlaceOrderAndWaitAsync(variantId, 3, OrderStatus.Rejected);

        var stockCheckSpan = await WaitForSpanAsync(span => IsSpanFor(span, OrderTelemetry.StockCheckSpanName, orderId));

        Assert.That(stockCheckSpan, Is.Not.Null);
        Assert.That(stockCheckSpan!.GetTagItem("order.outcome"), Is.EqualTo(OrderTelemetry.OutcomeRejected));
        Assert.That(stockCheckSpan.GetTagItem("order.rejection_reason"), Is.EqualTo(OrderTelemetry.RejectionReasonInsufficientStock));
    }

    private async Task<Guid> PlaceOrderAndWaitAsync(Guid variantId, int quantity, OrderStatus expectedStatus)
    {
        var response = await Client.PostAsJsonAsync("/api/orders", new CreateOrderRequest { VariantId = variantId, Quantity = quantity });
        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>();

        var resolved = await PollUntilResolvedAsync(accepted!.OrderId, TimeSpan.FromSeconds(30));
        Assert.That(resolved.Status, Is.EqualTo(expectedStatus));

        return accepted.OrderId;
    }

    private static bool IsSpanFor(Activity span, string name, Guid orderId) =>
        span.DisplayName == name && span.GetTagItem("order.id") as string == orderId.ToString();
}
