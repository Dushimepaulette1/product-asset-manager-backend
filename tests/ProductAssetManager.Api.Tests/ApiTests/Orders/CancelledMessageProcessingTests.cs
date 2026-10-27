using System.Net.Http.Headers;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

[TestFixture]
public class CancelledMessageProcessingTests : ApiTestBase
{
    private const string Sku = "CANCELLED-MESSAGE-SKU";
    private const string SkippedTemplate = "Order {OrderId} was cancelled before processing; skipping without changing stock";

    [SetUp]
    public async Task SignInAsShopper()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());
    }

    [Test]
    public async Task When_OrderIsCancelledBeforeTheConsumerReadsIt_Should_SkipItWithoutTouchingStockAndCompleteTheMessage()
    {
        var variantId = await SeedVariantAsync(Sku, 5);

        var cancelledOrderId = await PlacePendingOrderAsync(variantId, 2);
        var laterOrderId = await PlacePendingOrderAsync(variantId, 1);
        await SetOrderStatusAsync(cancelledOrderId, OrderStatus.Cancelled);

        await ResumeConsumerAsync();

        var later = await PollUntilResolvedAsync(laterOrderId, TimeSpan.FromSeconds(30));
        Assert.That(later.Status, Is.EqualTo(OrderStatus.Confirmed), "the cancelled order's message is ahead in the same SKU session, so this resolves only if that message was completed");

        Assert.That(await GetOrderStatusAsync(cancelledOrderId), Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(await GetVariantQuantityAsync(Sku), Is.EqualTo(4), "only the later order may take stock");
        Assert.That(HasLog(SkippedTemplate, cancelledOrderId), Is.True, "the consumer should log why it skipped the order");

        var processSpan = await WaitForOrderSpanAsync(OrderTelemetry.ProcessSpanName, cancelledOrderId);
        Assert.That(processSpan!.GetTagItem("order.outcome"), Is.EqualTo(OrderTelemetry.OutcomeSkippedCancelled));
        Assert.That(
            Factory.ExportedActivities.Snapshot().Any(span =>
                span.DisplayName == OrderTelemetry.StockCheckSpanName && span.GetTagItem("order.id") as string == cancelledOrderId.ToString()),
            Is.False,
            "a cancelled order never reaches the stock check");

        Assert.That(await FindStockDecrementedAsync(laterOrderId, TimeSpan.FromSeconds(30)), Is.Not.Null, "the later order's event shows the session has finished");
        Assert.That(await FindStockDecrementedAsync(cancelledOrderId, TimeSpan.Zero), Is.Null, "no StockDecremented may be published for a cancelled order");
    }
}
