using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Variants;

[TestFixture]
public class UpdateStockTests : ApiTestBase
{
    private Guid _productId;
    private const string SeededSku = "STOCK-TEST-SKU";

    [SetUp]
    public async Task AuthorizeAsAdminAndSeedProduct()
    {
        var token = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var categoryResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var productResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Stock Update Test Dress",
            Description = "For testing stock updates",
            BasePrice = 29.99m,
            Material = "Cotton",
            CategoryId = category!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "One Size", Sku = SeededSku, Quantity = 10 }
            }
        });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>();
        _productId = product!.Id;
    }

    [Test]
    public async Task When_UpdatingStockToZero_Should_ReflectOutOfStockOnPublicEndpointImmediately()
    {
        var updateResponse = await Client.PatchAsJsonAsync(
            $"/api/variants/{SeededSku}/stock",
            new UpdateStockRequest { Quantity = 0 });

        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Client.DefaultRequestHeaders.Authorization = null;
        var publicProduct = await Client.GetFromJsonAsync<PublicProductResponse>($"/api/products/{_productId}");

        Assert.That(publicProduct, Is.Not.Null);
        var variant = publicProduct!.Variants.Single(v => v.Sku == SeededSku);
        Assert.That(variant.StockStatus, Is.EqualTo("OUT_OF_STOCK"));
    }

    [Test]
    public async Task When_UpdatingStockWithNegativeQuantity_Should_ReturnBadRequest()
    {
        var response = await Client.PatchAsJsonAsync(
            $"/api/variants/{SeededSku}/stock",
            new UpdateStockRequest { Quantity = -1 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task When_UpdatingStockForNonExistentSku_Should_ReturnNotFound()
    {
        var response = await Client.PatchAsJsonAsync(
            "/api/variants/DOES-NOT-EXIST/stock",
            new UpdateStockRequest { Quantity = 5 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
