using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

[TestFixture]
public class AsyncPurchaseTests : ApiTestBase
{
    private const string SeededSku = "ASYNC-PURCHASE-TEST-SKU";
    private Guid _variantId;

    [SetUp]
    public async Task SeedProductAuthorizeAsUserAndStartConsumer()
    {
        var adminToken = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var categoryResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var productResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Async Purchase Test Dress",
            Description = "For testing the full async purchase flow",
            BasePrice = 35.00m,
            Material = "Cotton",
            CategoryId = category!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "One Size", Sku = SeededSku, Quantity = 5 }
            }
        });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>();
        _variantId = product!.Variants.Single(v => v.Sku == SeededSku).Id;

        var userToken = await CreateUserTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        await ResumeConsumerAsync();
    }

    [Test]
    public async Task When_PurchasingInStockVariant_Should_ReturnAcceptedThenResolveToConfirmedWithReducedStock()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest { VariantId = _variantId, Quantity = 2 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>();
        Assert.That(accepted, Is.Not.Null);

        var resolved = await PollUntilResolvedAsync(accepted!.OrderId);

        Assert.That(resolved.Status, Is.EqualTo(OrderStatus.Confirmed));

        var remainingQuantity = await GetVariantQuantityAsync(SeededSku);
        Assert.That(remainingQuantity, Is.EqualTo(3));
    }
}
