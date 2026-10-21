using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

[TestFixture]
public class LoadBufferingTests : ApiTestBase
{
    private const string SeededSku = "LOAD-TEST-SKU";
    private const int SeededQuantity = 6;
    private const int PurchaseAttempts = 20;
    private Guid _variantId;

    [SetUp]
    public async Task SeedLowStockProductAuthorizeAsUserAndStartConsumer()
    {
        var adminToken = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var categoryResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var productResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Load Buffering Test Dress",
            Description = "For proving no overselling holds under concurrent load",
            BasePrice = 20.00m,
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

        await ResumeConsumerAsync();
    }

    [Test]
    public async Task When_FiringTwentyConcurrentPurchasesAgainstSixInStock_Should_ConfirmExactlySixAndRejectTheRest()
    {
        var purchaseTasks = Enumerable.Range(0, PurchaseAttempts)
            .Select(_ => Client.PostAsJsonAsync(
                "/api/orders",
                new CreateOrderRequest { VariantId = _variantId, Quantity = 1 }))
            .ToArray();

        var responses = await Task.WhenAll(purchaseTasks);

        Assert.That(responses.All(r => r.StatusCode == HttpStatusCode.Accepted), Is.True,
            "every purchase attempt should be accepted immediately - the stock check happens later, in the Consumer");

        var acceptedBodies = await Task.WhenAll(
            responses.Select(r => r.Content.ReadFromJsonAsync<OrderAcceptedResponse>(JsonTestOptions.Default)));
        var orderIds = acceptedBodies.Select(a => a!.OrderId).ToArray();

        Assert.That(orderIds, Has.Length.EqualTo(PurchaseAttempts));

        var resolveTasks = orderIds.Select(id => PollUntilResolvedAsync(id, TimeSpan.FromSeconds(30)));
        var resolvedOrders = await Task.WhenAll(resolveTasks);

        var confirmedCount = resolvedOrders.Count(o => o.Status == OrderStatus.Confirmed);
        var rejectedCount = resolvedOrders.Count(o => o.Status == OrderStatus.Rejected);

        Assert.That(confirmedCount, Is.EqualTo(SeededQuantity), "exactly the seeded quantity should be confirmed");
        Assert.That(rejectedCount, Is.EqualTo(PurchaseAttempts - SeededQuantity), "everyone else should be rejected");

        var finalQuantity = await GetVariantQuantityAsync(SeededSku);
        Assert.That(finalQuantity, Is.EqualTo(0), "stock must land at exactly zero - never negative, never left over");
    }
}
