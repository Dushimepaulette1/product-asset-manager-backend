using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

[TestFixture]
public class PurchaseTests : ApiTestBase
{
    private const string SeededSku = "PURCHASE-TEST-SKU";
    private Guid _variantId;
    private string _userToken = string.Empty;

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
            Name = "Purchase Test Dress",
            Description = "For testing the purchase endpoint",
            BasePrice = 40.00m,
            Material = "Cotton",
            CategoryId = category!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "One Size", Sku = SeededSku, Quantity = 5 }
            }
        });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>();
        _variantId = product!.Variants.Single(v => v.Sku == SeededSku).Id;

        _userToken = await CreateUserTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
    }

    [Test]
    public async Task When_PurchasingInStockVariant_Should_AcceptImmediatelyAndLeaveOrderPending()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest { VariantId = _variantId, Quantity = 2 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>();

        Assert.That(accepted, Is.Not.Null);
        Assert.That(accepted!.OrderId, Is.Not.EqualTo(Guid.Empty));

        var status = await GetOrderStatusAsync(accepted.OrderId);
        Assert.That(status, Is.EqualTo(OrderStatus.Pending));

        var remainingQuantity = await GetVariantQuantityAsync(SeededSku);
        Assert.That(remainingQuantity, Is.EqualTo(5), "the Producer no longer touches stock - that now only happens once the Consumer (Card 5) processes the message");
    }

    [Test]
    public async Task When_PurchasingMoreThanAvailableQuantity_Should_StillAcceptSinceStockCheckMovedToConsumer()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest { VariantId = _variantId, Quantity = 10 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>();
        var status = await GetOrderStatusAsync(accepted!.OrderId);
        Assert.That(status, Is.EqualTo(OrderStatus.Pending));

        var remainingQuantity = await GetVariantQuantityAsync(SeededSku);
        Assert.That(remainingQuantity, Is.EqualTo(5));
    }

    [Test]
    public async Task When_PurchasingWithoutAuthentication_Should_ReturnUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest { VariantId = _variantId, Quantity = 1 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
