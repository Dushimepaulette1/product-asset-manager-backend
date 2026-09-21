using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Orders;

[TestFixture]
public class GetOrderTests : ApiTestBase
{
    private const string SeededSku = "GET-ORDER-TEST-SKU";
    private string _adminToken = string.Empty;
    private string _ownerToken = string.Empty;
    private Guid _orderId;

    [SetUp]
    public async Task SeedProductAndPlaceOrderAsOwner()
    {
        _adminToken = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var categoryResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var productResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Get Order Test Dress",
            Description = "For testing order lookup",
            BasePrice = 30.00m,
            Material = "Cotton",
            CategoryId = category!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "One Size", Sku = SeededSku, Quantity = 5 }
            }
        });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>();
        var variantId = product!.Variants.Single(v => v.Sku == SeededSku).Id;

        _ownerToken = await CreateUserTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _ownerToken);

        var orderResponse = await Client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest { VariantId = variantId, Quantity = 1 });
        var accepted = await orderResponse.Content.ReadFromJsonAsync<OrderAcceptedResponse>();
        _orderId = accepted!.OrderId;
    }

    [Test]
    public async Task When_OwnerRequestsTheirOwnOrder_Should_ReturnOkWithStatus()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _ownerToken);

        var response = await Client.GetAsync($"/api/orders/{_orderId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonTestOptions.Default);

        Assert.That(order, Is.Not.Null);
        Assert.That(order!.Id, Is.EqualTo(_orderId));
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Pending));
        Assert.That(order.VariantSku, Is.EqualTo(SeededSku));
    }

    [Test]
    public async Task When_AdminRequestsAnyUsersOrder_Should_ReturnOk()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

        var response = await Client.GetAsync($"/api/orders/{_orderId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task When_DifferentUserRequestsSomeoneElsesOrder_Should_ReturnForbidden()
    {
        var otherUserToken = await CreateUserTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherUserToken);

        var response = await Client.GetAsync($"/api/orders/{_orderId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task When_RequestingNonExistentOrderId_Should_ReturnNotFound()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _ownerToken);

        var response = await Client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
