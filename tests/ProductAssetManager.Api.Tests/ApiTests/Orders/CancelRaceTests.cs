using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

// Each race is forced with BeforeSaveHook: the competing write commits inside the other side's SaveChanges, after it
// read the rows and before its UPDATEs run. Nothing depends on timing, except the last test, which only checks
// invariants that must hold whichever side wins.
[TestFixture]
public class CancelRaceTests : ApiTestBase
{
    private const string Sku = "CANCEL-RACE-SKU";
    private const string SkippedTemplate = "Order {OrderId} was cancelled before processing; skipping without changing stock";
    private const string ConflictTemplate = "Concurrency conflict saving order {OrderId} on attempt {Attempt} ({ConflictingEntities}); reloading the order and its variant";
    private const string UnhandledExceptionTemplate = "Unhandled exception while processing {Method} {Path}";

    [SetUp]
    public async Task SignInAsShopper()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());
    }

    [Test]
    public async Task When_TheConsumerConfirmsWhileTheCancellationSaves_Should_ReturnConflictNotAServerError()
    {
        using var metrics = ListenToProcessedOrders();
        var variantId = await SeedVariantAsync(Sku, 5);
        var orderId = await PlacePendingOrderAsync(variantId, 1);

        Factory.BeforeSaveHook.ArmOnce(
            when: context => IsSavingOrderAs(context, orderId, OrderStatus.Cancelled),
            run: async () =>
            {
                await ResumeConsumerAsync();
                await WaitForStatusAsync(orderId, OrderStatus.Confirmed);
            });

        var response = await CancelOrderAsync(orderId);

        Assert.That(Factory.BeforeSaveHook.HasFired, Is.True, "the consumer must confirm inside the cancellation's save, or this test proves nothing");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain($"Order '{orderId}' is Confirmed and can no longer be cancelled."));
        Assert.That(await GetOrderStatusAsync(orderId), Is.EqualTo(OrderStatus.Confirmed), "the cancellation must not overwrite the consumer's decision");
        Assert.That(await GetVariantQuantityAsync(Sku), Is.EqualTo(4));
        Assert.That(Factory.CapturedLogs.Snapshot().Any(log => log.Body == UnhandledExceptionTemplate), Is.False, "the concurrency conflict must be handled, never reach the global 500 handler");

        await metrics.WaitForTotalAsync(1, TimeSpan.FromSeconds(10));
        Assert.That(metrics.Count(OrderTelemetry.OutcomeConfirmed), Is.EqualTo(1));
        Assert.That(metrics.Count(OrderTelemetry.OutcomeCancelled), Is.Zero, "a cancellation that lost the race is not counted");
    }

    [Test]
    public async Task When_TheOrderIsDeletedWhileTheCancellationSaves_Should_ReturnNotFound()
    {
        using var metrics = ListenToProcessedOrders();
        var variantId = await SeedVariantAsync(Sku, 5);
        var orderId = await PlacePendingOrderAsync(variantId, 1);

        Factory.BeforeSaveHook.ArmOnce(
            when: context => IsSavingOrderAs(context, orderId, OrderStatus.Cancelled),
            run: () => DeleteOrderAsync(orderId));

        var response = await CancelOrderAsync(orderId);

        Assert.That(Factory.BeforeSaveHook.HasFired, Is.True);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(metrics.Count(OrderTelemetry.OutcomeCancelled), Is.Zero);
    }

    [Test]
    public async Task When_AnUpdateLeavesTheOrderPendingWhileTheCancellationSaves_Should_RetryAndCancel()
    {
        using var metrics = ListenToProcessedOrders();
        var variantId = await SeedVariantAsync(Sku, 5);
        var orderId = await PlacePendingOrderAsync(variantId, 1);

        Factory.BeforeSaveHook.ArmOnce(
            when: context => IsSavingOrderAs(context, orderId, OrderStatus.Cancelled),
            run: () => BumpRowVersionAsync(orderId));

        var response = await CancelOrderAsync(orderId);

        Assert.That(Factory.BeforeSaveHook.HasFired, Is.True, "the RowVersion must change inside the cancellation's save, or this test proves nothing");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the order was still Pending after the conflict, so the cancellation retries instead of answering 409");
        Assert.That(await GetOrderStatusAsync(orderId), Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(metrics.Count(OrderTelemetry.OutcomeCancelled), Is.EqualTo(1), "counted once, after the save that succeeded");
        Assert.That(metrics.Total, Is.EqualTo(1));
    }

    [Test]
    public async Task When_EveryCancellationAttemptConflicts_Should_ReturnAControlledConflictAndLeaveTheOrderPending()
    {
        const int maxAttempts = 3;
        using var metrics = ListenToProcessedOrders();
        var variantId = await SeedVariantAsync(Sku, 5);
        var orderId = await PlacePendingOrderAsync(variantId, 1);

        // The hook fires once per arming, so each bump re-arms it for the next attempt's save.
        var bumps = 0;
        Func<Task> bumpAndRearm = null!;
        bumpAndRearm = async () =>
        {
            await BumpRowVersionAsync(orderId);

            if (++bumps < maxAttempts)
            {
                Factory.BeforeSaveHook.ArmOnce(context => IsSavingOrderAs(context, orderId, OrderStatus.Cancelled), bumpAndRearm);
            }
        };
        Factory.BeforeSaveHook.ArmOnce(context => IsSavingOrderAs(context, orderId, OrderStatus.Cancelled), bumpAndRearm);

        var response = await CancelOrderAsync(orderId);

        Assert.That(bumps, Is.EqualTo(maxAttempts), "every attempt must have hit a conflict");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain($"Order '{orderId}' kept changing while it was being cancelled. Please try again."));
        Assert.That(Factory.CapturedLogs.Snapshot().Any(log => log.Body == UnhandledExceptionTemplate), Is.False, "running out of retries must not become a 500");
        Assert.That(await GetOrderStatusAsync(orderId), Is.EqualTo(OrderStatus.Pending));
        Assert.That(metrics.Total, Is.Zero, "nothing was saved, so nothing is counted");
    }

    [Test]
    public async Task When_OrderIsCancelledWhileTheConsumerSavesIt_Should_RollBackTheDecrementAndSkipTheOrder()
    {
        using var metrics = ListenToProcessedOrders();
        var variantId = await SeedVariantAsync(Sku, 5);
        var racedOrderId = await PlacePendingOrderAsync(variantId, 2);
        var laterOrderId = await PlacePendingOrderAsync(variantId, 1);

        HttpStatusCode? cancelStatus = null;
        Factory.BeforeSaveHook.ArmOnce(
            when: context => IsSavingOrderAs(context, racedOrderId, OrderStatus.Confirmed),
            run: async () => cancelStatus = (await CancelOrderAsync(racedOrderId)).StatusCode);

        await ResumeConsumerAsync();

        var later = await PollUntilResolvedAsync(laterOrderId, TimeSpan.FromSeconds(30));

        Assert.That(Factory.BeforeSaveHook.HasFired, Is.True, "the cancellation must land inside the consumer's save, or this test proves nothing");
        Assert.That(cancelStatus, Is.EqualTo(HttpStatusCode.OK), "the API cancellation commits first, while the consumer's save is paused");
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

        await metrics.WaitForTotalAsync(2, TimeSpan.FromSeconds(10));
        Assert.That(metrics.Count(OrderTelemetry.OutcomeCancelled), Is.EqualTo(1));
        Assert.That(metrics.Count(OrderTelemetry.OutcomeConfirmed), Is.EqualTo(1), "only the later order is confirmed; the consumer's skip of the raced order counts nothing");
        Assert.That(metrics.Total, Is.EqualTo(2));
    }

    [Test]
    public async Task When_TwoContextsSaveTheSameLoadedOrder_Should_RejectTheSecondSaveAndKeepTheFirstOutcome()
    {
        var variantId = await SeedVariantAsync(Sku, 5);
        var orderId = await PlacePendingOrderAsync(variantId, 1);

        using var confirmingScope = Factory.Services.CreateScope();
        using var cancellingScope = Factory.Services.CreateScope();
        var confirmingContext = confirmingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cancellingContext = cancellingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That(cancellingContext, Is.Not.SameAs(confirmingContext));

        var orderToConfirm = await confirmingContext.Orders.SingleAsync(o => o.Id == orderId);
        var orderToCancel = await cancellingContext.Orders.SingleAsync(o => o.Id == orderId);
        Assert.That(orderToConfirm.Status, Is.EqualTo(OrderStatus.Pending));
        Assert.That(orderToCancel.Status, Is.EqualTo(OrderStatus.Pending));

        orderToConfirm.Status = OrderStatus.Confirmed;
        await confirmingContext.SaveChangesAsync();

        orderToCancel.Status = OrderStatus.Cancelled;
        Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => cancellingContext.SaveChangesAsync(),
            "the cancelling context still holds the RowVersion it read before the confirmation committed");

        Assert.That(await GetOrderStatusAsync(orderId), Is.EqualTo(OrderStatus.Confirmed), "the first save's outcome is kept");
    }

    [Test]
    public async Task When_ManyOrdersAreCancelledWhileTheConsumerRuns_Should_GiveEveryOrderExactlyOneConsistentOutcome()
    {
        const int orderCount = 20;
        const int seededStock = 10;

        using var metrics = ListenToProcessedOrders();
        var variantId = await SeedVariantAsync(Sku, seededStock);

        var orderIds = new List<Guid>();
        for (var i = 0; i < orderCount; i++)
        {
            orderIds.Add(await PlacePendingOrderAsync(variantId, 1));
        }

        // Fire the cancels only once the consumer is visibly working through the queue, so they race it instead of
        // all landing before it starts. The first order is then decided already and must get a 409.
        await ResumeConsumerAsync();
        await PollUntilResolvedAsync(orderIds[0], TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(20));
        var responses = await Task.WhenAll(orderIds.Select(id => CancelOrderAsync(id)));

        await Task.WhenAll(orderIds.Select(id => PollUntilResolvedAsync(id, TimeSpan.FromSeconds(60))));

        var cancelledCount = 0;
        var confirmedCount = 0;
        for (var i = 0; i < orderCount; i++)
        {
            var status = await GetOrderStatusAsync(orderIds[i]);

            if (responses[i].StatusCode == HttpStatusCode.OK)
            {
                Assert.That(status, Is.EqualTo(OrderStatus.Cancelled), "a 200 means the cancellation was saved");
                cancelledCount++;
            }
            else
            {
                Assert.That(responses[i].StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "every cancel answers 200 or 409, never 500");
                Assert.That(status, Is.EqualTo(OrderStatus.Confirmed).Or.EqualTo(OrderStatus.Rejected), "a 409 means the consumer decided first");
            }

            if (status == OrderStatus.Confirmed)
            {
                confirmedCount++;
            }
        }

        Assert.That(responses[0].StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "the first order was decided before any cancel was sent");
        Assert.That(await GetVariantQuantityAsync(Sku), Is.EqualTo(seededStock - confirmedCount), "stock drops only for confirmed orders, never for cancelled ones");

        await metrics.WaitForTotalAsync(orderCount, TimeSpan.FromSeconds(30));
        Assert.That(metrics.Total, Is.EqualTo(orderCount), "every order is counted exactly once");
        Assert.That(metrics.Count(OrderTelemetry.OutcomeCancelled), Is.EqualTo(cancelledCount));
        Assert.That(metrics.Count(OrderTelemetry.OutcomeConfirmed), Is.EqualTo(confirmedCount));
        TestContext.Out.WriteLine($"Cancelled {cancelledCount}, confirmed {confirmedCount}, rejected {orderCount - cancelledCount - confirmedCount}");
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

    private async Task WaitForStatusAsync(Guid orderId, OrderStatus status)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (await GetOrderStatusAsync(orderId) != status && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
    }

    // A no-op UPDATE still gives the row a new RowVersion, standing in for any update that leaves the status alone.
    private async Task BumpRowVersionAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await dbContext.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(setters => setters.SetProperty(o => o.OrderDate, o => o.OrderDate));
    }

    private async Task DeleteOrderAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await dbContext.Orders.Where(o => o.Id == orderId).ExecuteDeleteAsync();
    }

    private bool HasConflictLog(Guid orderId, string conflictingEntities) =>
        Factory.CapturedLogs.Snapshot().Any(log =>
            log.Body == ConflictTemplate
            && Equals(log.Attributes.GetValueOrDefault("OrderId"), orderId)
            && Equals(log.Attributes.GetValueOrDefault("ConflictingEntities"), conflictingEntities));
}
