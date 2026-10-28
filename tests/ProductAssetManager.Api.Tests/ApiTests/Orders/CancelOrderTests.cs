using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Observability;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

[TestFixture]
public class CancelOrderTests : ApiTestBase
{
    private const string Sku = "CANCEL-ORDER-SKU";
    private const int SeededQuantity = 5;
    private Guid _variantId;

    [SetUp]
    public async Task SeedVariantAndSignInAsShopper()
    {
        _variantId = await SeedVariantAsync(Sku, SeededQuantity);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());
    }

    [Test]
    public async Task When_OwnerCancelsTheirPendingOrder_Should_ReturnOkWithTheCancelledOrderAndLeaveStockUntouched()
    {
        using var metrics = ListenToProcessedOrders();
        var orderId = await PlacePendingOrderAsync(_variantId, 2);

        var response = await CancelOrderAsync(orderId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cancelled = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonTestOptions.Default);
        Assert.That(cancelled!.Id, Is.EqualTo(orderId));
        Assert.That(cancelled.Status, Is.EqualTo(OrderStatus.Cancelled));
        Assert.That(cancelled.VariantSku, Is.EqualTo(Sku));
        Assert.That(cancelled.QuantityPurchased, Is.EqualTo(2));
        Assert.That(cancelled.TotalPrice, Is.EqualTo(cancelled.UnitPriceAtPurchase * 2));
        Assert.That(cancelled.RejectionReason, Is.Null);

        var fetched = await Client.GetFromJsonAsync<OrderResponse>($"/api/orders/{orderId}", JsonTestOptions.Default);
        Assert.That(fetched!.Status, Is.EqualTo(OrderStatus.Cancelled), "GET must return the cancelled status");

        Assert.That(await GetVariantQuantityAsync(Sku), Is.EqualTo(SeededQuantity), "cancelling never touches stock");
        Assert.That(metrics.Count(OrderTelemetry.OutcomeCancelled), Is.EqualTo(1));
        Assert.That(metrics.Total, Is.EqualTo(1));
    }

    [Test]
    public async Task When_AnotherUserCancelsTheOrder_Should_ReturnNotFoundLikeAnUnknownOrder()
    {
        using var metrics = ListenToProcessedOrders();
        var orderId = await PlacePendingOrderAsync(_variantId, 1);

        using var otherShopper = Factory.CreateClient();
        otherShopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateUserTokenAsync());

        var response = await CancelOrderAsync(orderId, otherShopper);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadMessageAsync(response), Is.EqualTo($"Order '{orderId}' was not found."), "the same answer as for an order that does not exist, so the order's existence is not revealed");
        Assert.That(await GetOrderStatusAsync(orderId), Is.EqualTo(OrderStatus.Pending));
        Assert.That(metrics.Total, Is.Zero);
    }

    [Test]
    public async Task When_AnAdminCancelsSomeoneElsesOrder_Should_ReturnNotFound()
    {
        var orderId = await PlacePendingOrderAsync(_variantId, 1);

        using var admin = Factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await CreateAdminTokenAsync());

        var response = await CancelOrderAsync(orderId, admin);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "cancelling is limited to the order's owner; admins get no override");
        Assert.That(await GetOrderStatusAsync(orderId), Is.EqualTo(OrderStatus.Pending));
    }

    [Test]
    public async Task When_OrderDoesNotExist_Should_ReturnNotFound()
    {
        var unknownId = Guid.NewGuid();

        var response = await CancelOrderAsync(unknownId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadMessageAsync(response), Is.EqualTo($"Order '{unknownId}' was not found."));
    }

    [Test]
    public async Task When_NotAuthenticated_Should_ReturnUnauthorized()
    {
        var orderId = await PlacePendingOrderAsync(_variantId, 1);
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await CancelOrderAsync(orderId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await GetOrderStatusAsync(orderId), Is.EqualTo(OrderStatus.Pending));
    }

    [TestCase(OrderStatus.Confirmed, 1)]
    [TestCase(OrderStatus.Rejected, SeededQuantity + 1)]
    public async Task When_TheConsumerAlreadyDecided_Should_ReturnConflictAndLeaveTheOrderUnchanged(OrderStatus decided, int quantity)
    {
        await ResumeConsumerAsync();
        var orderId = await PlaceOrderAndWaitForStatusAsync(_variantId, quantity, decided);
        var stockBefore = await GetVariantQuantityAsync(Sku);

        using var metrics = ListenToProcessedOrders();
        var response = await CancelOrderAsync(orderId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await ReadMessageAsync(response), Is.EqualTo($"Order '{orderId}' is {decided} and can no longer be cancelled."));
        Assert.That(await GetOrderStatusAsync(orderId), Is.EqualTo(decided));
        Assert.That(await GetVariantQuantityAsync(Sku), Is.EqualTo(stockBefore));
        Assert.That(metrics.Count(OrderTelemetry.OutcomeCancelled), Is.Zero);
    }

    [Test]
    public async Task When_OrderIsCancelledTwice_Should_ReturnConflictTheSecondTimeAndCountOneCancellation()
    {
        using var metrics = ListenToProcessedOrders();
        var orderId = await PlacePendingOrderAsync(_variantId, 1);

        var first = await CancelOrderAsync(orderId);
        var second = await CancelOrderAsync(orderId);

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await ReadMessageAsync(second), Is.EqualTo($"Order '{orderId}' is Cancelled and can no longer be cancelled."));
        Assert.That(metrics.Count(OrderTelemetry.OutcomeCancelled), Is.EqualTo(1), "only the cancellation that was saved is counted");
    }

    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<MessageBody>(JsonTestOptions.Default))?.Message;

    private record MessageBody(string Message);
}
