using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

// Each race is forced with BeforeSaveHook: the competing write commits inside the consumer's SaveChanges, after it
// read the rows and before its UPDATEs run. Nothing depends on timing.
[TestFixture]
public class CancelRaceTests : ApiTestBase
{
    private const string Sku = "CANCEL-RACE-SKU";
    private const string SkippedTemplate = "Order {OrderId} was cancelled before processing; skipping without changing stock";
    private const string ConflictTemplate = "Concurrency conflict saving order {OrderId} on attempt {Attempt} ({ConflictingEntities}); reloading the order and its variant";

    [SetUp]
    public async Task SignInAsShopper()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());
    }

    [Test]
    public async Task When_OrderIsCancelledWhileTheConsumerSavesIt_Should_RollBackTheDecrementAndSkipTheOrder()
    {
        var variantId = await SeedVariantAsync(Sku, 5);
        var racedOrderId = await PlacePendingOrderAsync(variantId, 2);
        var laterOrderId = await PlacePendingOrderAsync(variantId, 1);

        Factory.BeforeSaveHook.ArmOnce(
            when: context => IsSavingOrderAs(context, racedOrderId, OrderStatus.Confirmed),
            run: () => SetOrderStatusAsync(racedOrderId, OrderStatus.Cancelled));

        await ResumeConsumerAsync();

        var later = await PollUntilResolvedAsync(laterOrderId, TimeSpan.FromSeconds(30));

        Assert.That(Factory.BeforeSaveHook.HasFired, Is.True, "the cancellation must land inside the consumer's save, or this test proves nothing");
        Assert.That(later.Status, Is.EqualTo(OrderStatus.Confirmed), "the raced order's message must be completed, not left blocking the session");
        Assert.That(await GetOrderStatusAsync(racedOrderId), Is.EqualTo(OrderStatus.Cancelled), "the consumer must not overwrite the cancellation");
        Assert.That(await GetVariantQuantityAsync(Sku), Is.EqualTo(4), "the raced order's decrement was rolled back; only the later order took stock");

        Assert.That(HasConflictLog(racedOrderId, "Order"), Is.True);
        Assert.That(HasLog(SkippedTemplate, racedOrderId), Is.True);

        var stockCheckSpan = await WaitForOrderSpanAsync(OrderTelemetry.StockCheckSpanName, racedOrderId);
        Assert.That(stockCheckSpan!.GetTagItem("order.outcome"), Is.EqualTo(OrderTelemetry.OutcomeSkippedCancelled));
        Assert.That(stockCheckSpan.GetTagItem("order.save_attempts"), Is.EqualTo(1), "one save was tried; the next attempt saw Cancelled and stopped without saving");

        Assert.That(await FindStockDecrementedAsync(laterOrderId, TimeSpan.FromSeconds(30)), Is.Not.Null, "the later order's event shows the session has finished");
        Assert.That(await FindStockDecrementedAsync(racedOrderId, TimeSpan.Zero), Is.Null, "no StockDecremented may be published for a cancelled order");
    }

    [Test]
    public async Task When_StockChangesWhileTheConsumerSavesAnOrder_Should_ReloadAndDecideAgainWithTheNewStock()
    {
        using var adminClient = Factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateAdminTokenAsync());

        var variantId = await SeedVariantAsync(Sku, 5);
        var orderId = await PlacePendingOrderAsync(variantId, 3);

        Factory.BeforeSaveHook.ArmOnce(
            when: context => IsSavingOrderAs(context, orderId, OrderStatus.Confirmed),
            run: async () =>
            {
                var response = await adminClient.PatchAsJsonAsync($"/api/variants/{Sku}/stock", new UpdateStockRequest { Quantity = 2 });
                response.EnsureSuccessStatusCode();
            });

        await ResumeConsumerAsync();

        var resolved = await PollUntilResolvedAsync(orderId, TimeSpan.FromSeconds(30));

        Assert.That(Factory.BeforeSaveHook.HasFired, Is.True, "the stock edit must land inside the consumer's save, or this test proves nothing");
        Assert.That(resolved.Status, Is.EqualTo(OrderStatus.Rejected), "after reloading, the order was still Pending, so the consumer decided again with 2 in stock");
        Assert.That(await GetVariantQuantityAsync(Sku), Is.EqualTo(2), "the first attempt's in-memory decrement must be discarded, not applied on top of the new stock");
        Assert.That(HasConflictLog(orderId, "Variant"), Is.True);

        var stockCheckSpan = await WaitForOrderSpanAsync(OrderTelemetry.StockCheckSpanName, orderId);
        Assert.That(stockCheckSpan!.GetTagItem("order.outcome"), Is.EqualTo(OrderTelemetry.OutcomeRejected));
        Assert.That(stockCheckSpan.GetTagItem("order.save_attempts"), Is.EqualTo(2));
    }

    private bool HasConflictLog(Guid orderId, string conflictingEntities) =>
        Factory.CapturedLogs.Snapshot().Any(log =>
            log.Body == ConflictTemplate
            && Equals(log.Attributes.GetValueOrDefault("OrderId"), orderId)
            && Equals(log.Attributes.GetValueOrDefault("ConflictingEntities"), conflictingEntities));
}
