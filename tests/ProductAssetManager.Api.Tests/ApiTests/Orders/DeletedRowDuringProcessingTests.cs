using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.Data;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

// The consumer reloads the order and its variant after a concurrency conflict. These tests force a row to disappear
// inside the consumer's save (via BeforeSaveHook) and check how each case settles the Service Bus message.
[TestFixture]
public class DeletedRowDuringProcessingTests : ApiTestBase
{
    private const string Sku = "DELETED-ROW-SKU";
    private const string OrderDeletedTemplate = "Order {OrderId} from message {MessageId} was deleted while it was being processed; completing the message because retrying cannot help";
    private const string OrderNotFoundTemplate = "Order {OrderId} from message {MessageId} was not found; completing the message because retrying cannot help";
    private const string ProcessorErrorTemplate = "Service Bus session processor error";

    [SetUp]
    public async Task SignInAsShopper()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());
    }

    [Test]
    public async Task When_OrderIsDeletedWhileTheConsumerSavesIt_Should_RollBackTheDecrementAndCompleteTheMessage()
    {
        var variantId = await SeedVariantAsync(Sku, 5);
        var deletedOrderId = await PlacePendingOrderAsync(variantId, 2);
        var laterOrderId = await PlacePendingOrderAsync(variantId, 1);

        Factory.BeforeSaveHook.ArmOnce(
            when: context => IsSavingOrderAs(context, deletedOrderId, OrderStatus.Confirmed),
            run: () => DeleteOrderAsync(deletedOrderId));

        await ResumeConsumerAsync();

        var later = await PollUntilResolvedAsync(laterOrderId, TimeSpan.FromSeconds(30));

        Assert.That(Factory.BeforeSaveHook.HasFired, Is.True, "the deletion must land inside the consumer's save, or this test proves nothing");
        Assert.That(later.Status, Is.EqualTo(OrderStatus.Confirmed), "the deleted order's message is ahead in the same SKU session, so this resolves only if that message was completed");
        Assert.That(await OrderExistsAsync(deletedOrderId), Is.False);
        Assert.That(await GetVariantQuantityAsync(Sku), Is.EqualTo(4), "the deleted order's decrement was rolled back; only the later order took stock");
        Assert.That(HasLog(OrderDeletedTemplate, deletedOrderId), Is.True);
        Assert.That(ProcessSpansFor(deletedOrderId), Has.Count.EqualTo(1), "the message was completed on its first delivery, not redelivered");

        Assert.That(await FindStockDecrementedAsync(laterOrderId, TimeSpan.FromSeconds(30)), Is.Not.Null, "the later order's event shows the session has finished");
        Assert.That(await FindStockDecrementedAsync(deletedOrderId, TimeSpan.Zero), Is.Null, "no StockDecremented may be published for a deleted order");
    }

    [Test]
    public async Task When_AVariantHasOrders_Should_RefuseToDeleteIt()
    {
        var variantId = await SeedVariantAsync(Sku, 5);
        await PlacePendingOrderAsync(variantId, 1);

        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Variants.Remove(await dbContext.Variants.FirstAsync(v => v.Id == variantId));

        Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync(),
            "the Restrict foreign key from Orders is why the consumer never finds an order's variant missing in practice");
    }

    // A missing variant cannot happen with referential integrity intact: the Restrict foreign key from Orders blocks
    // deleting a variant that has orders (see the test above). The scenario is only reachable by disabling that
    // constraint, so this test does that on purpose to pin down what the consumer does if integrity is ever broken:
    // reloading a variant that a tracked order requires throws inside EF Core, so the delivery fails and the message
    // is redelivered instead of completed. On redelivery the order no longer loads with its variant, so the existing
    // "not found" path completes it.
    [Test]
    public async Task When_TheVariantIsMissingAfterAConflict_Should_FailTheDeliveryInsteadOfCompletingTheMessage()
    {
        var variantId = await SeedVariantAsync(Sku, 5);
        var orderId = await PlacePendingOrderAsync(variantId, 2);

        Factory.BeforeSaveHook.ArmOnce(
            when: context => IsSavingOrderAs(context, orderId, OrderStatus.Confirmed),
            run: () => DeleteVariantBypassingForeignKeyAsync(variantId));

        await ResumeConsumerAsync();

        await WaitUntilAsync(() => ProcessSpansFor(orderId).Count >= 2 && HasLog(OrderNotFoundTemplate, orderId), TimeSpan.FromSeconds(90));

        Assert.That(Factory.BeforeSaveHook.HasFired, Is.True, "the variant must disappear inside the consumer's save, or this test proves nothing");

        var processorError = Factory.CapturedLogs.Snapshot().SingleOrDefault(log =>
            log.Body == ProcessorErrorTemplate && log.Exception is InvalidOperationException);
        Assert.That(processorError, Is.Not.Null, "the first delivery must fail: reloading a missing variant that a tracked order requires throws");

        var deliveries = ProcessSpansFor(orderId);
        Assert.That(deliveries, Has.Count.GreaterThanOrEqualTo(2), "a second delivery of the same message proves the first was not completed");
        Assert.That(deliveries.Select(span => span.GetTagItem("messaging.message.id")).Distinct().Count(), Is.EqualTo(1), "both deliveries are the same message");

        Assert.That(await GetOrderStatusAsync(orderId), Is.EqualTo(OrderStatus.Pending), "a failed delivery must not confirm the order");
        Assert.That(await FindStockDecrementedAsync(orderId, TimeSpan.Zero), Is.Null);
    }

    private async Task DeleteOrderAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await dbContext.Orders.Where(o => o.Id == orderId).ExecuteDeleteAsync();
    }

    // Each test has its own database, so switching the constraint off cannot leak into other tests.
    private async Task DeleteVariantBypassingForeignKeyAsync(Guid variantId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await dbContext.Database.ExecuteSqlAsync(
            $"ALTER TABLE [Orders] NOCHECK CONSTRAINT [FK_Orders_Variants_VariantId]; DELETE FROM [Variants] WHERE [Id] = {variantId};");
    }

    private async Task<bool> OrderExistsAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Orders.AnyAsync(o => o.Id == orderId);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }
    }
}
