using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Messaging;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

[TestFixture]
public class ConsumerRecoveryTests : ApiTestBase
{
    private const string SeededSku = "RECOVERY-TEST-SKU";
    private const int SeededQuantity = 10;
    private const int PurchasesWhileStopped = 4;
    private Guid _variantId;

    [SetUp]
    public async Task SeedProductAndAuthorizeAsUser()
    {
        var adminToken = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var categoryResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var productResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Consumer Recovery Test Dress",
            Description = "For proving a stopped consumer loses nothing and double-processes nothing",
            BasePrice = 25.00m,
            Material = "Cotton",
            CategoryId = category!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "One Size", Sku = SeededSku, Quantity = SeededQuantity }
            }
        });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>();
        _variantId = product!.Variants.Single(v => v.Sku == SeededSku).Id;

        var userToken = await CreateUserTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
    }

    [Test]
    public async Task When_ConsumerIsStoppedAndRestarted_Should_ResolveEveryPendingOrderExactlyOnce()
    {
        var consumer = Factory.Services.GetRequiredService<OrderConsumer>();

        await consumer.ResumeProcessingAsync();

        var firstOrderId = await PlaceOrderAsync();
        var firstResolved = await PollUntilResolvedAsync(firstOrderId, TimeSpan.FromSeconds(30));
        Assert.That(firstResolved.Status, Is.EqualTo(OrderStatus.Confirmed), "the consumer must be working before it is stopped");

        await consumer.PauseProcessingAsync();

        var pendingOrderIds = new List<Guid>();
        for (var i = 0; i < PurchasesWhileStopped; i++)
        {
            pendingOrderIds.Add(await PlaceOrderAsync());
        }

        await Task.Delay(TimeSpan.FromSeconds(3));

        foreach (var orderId in pendingOrderIds)
        {
            var status = await GetOrderStatusAsync(orderId);
            Assert.That(status, Is.EqualTo(OrderStatus.Pending), "orders placed while the consumer is stopped must wait, not be processed or lost");
        }

        var stockWhileStopped = await GetVariantQuantityAsync(SeededSku);
        Assert.That(stockWhileStopped, Is.EqualTo(SeededQuantity - 1), "only the first order should have touched stock so far");

        await consumer.ResumeProcessingAsync();

        var resolvedOrders = await Task.WhenAll(
            pendingOrderIds.Select(id => PollUntilResolvedAsync(id, TimeSpan.FromSeconds(30))));

        Assert.That(resolvedOrders.All(o => o.Status == OrderStatus.Confirmed), Is.True, "every order held while stopped must be confirmed after the restart");

        var finalQuantity = await GetVariantQuantityAsync(SeededSku);
        var totalPurchased = 1 + PurchasesWhileStopped;
        Assert.That(finalQuantity, Is.EqualTo(SeededQuantity - totalPurchased), "stock must drop by exactly one unit per order - a double-processed order would push it lower");
    }

    private async Task<Guid> PlaceOrderAsync()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest { VariantId = _variantId, Quantity = 1 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>(JsonTestOptions.Default);
        return accepted!.OrderId;
    }
}
