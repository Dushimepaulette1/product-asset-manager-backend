using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
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
    public async Task When_PurchasingInStockVariant_Should_CreateOrderAndReduceStock()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest { VariantId = _variantId, Quantity = 2 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.That(order, Is.Not.Null);
        Assert.That(order!.QuantityPurchased, Is.EqualTo(2));
        Assert.That(order.VariantId, Is.EqualTo(_variantId));

        var remainingQuantity = await GetVariantQuantityAsync(SeededSku);
        Assert.That(remainingQuantity, Is.EqualTo(3));
    }

    [Test]
    public async Task When_PurchasingMoreThanAvailableQuantity_Should_RejectAndLeaveStockUnchanged()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest { VariantId = _variantId, Quantity = 10 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

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
