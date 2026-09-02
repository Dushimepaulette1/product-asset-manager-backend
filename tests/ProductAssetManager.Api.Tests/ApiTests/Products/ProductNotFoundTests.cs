using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Products;

[TestFixture]
public class ProductNotFoundTests : ApiTestBase
{
    private Guid _seededProductId;

    [SetUp]
    public async Task SeedKnownProduct()
    {
        var token = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var categoryResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var productResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Seeded Test Dress",
            Description = "A known product for Not Found tests",
            BasePrice = 39.99m,
            Material = "Cotton",
            CategoryId = category!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "One Size", Sku = "SEEDED-DRESS-1", Quantity = 10 }
            }
        });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>();
        _seededProductId = product!.Id;
    }

    [Test]
    public async Task When_RequestingExistingProductId_Should_ReturnOkWithProductData()
    {
        var response = await Client.GetAsync($"/api/products/{_seededProductId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<PublicProductResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Name, Is.EqualTo("Seeded Test Dress"));
        Assert.That(body.Variants, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task When_RequestingNonExistentProductId_Should_ReturnNotFound()
    {
        var nonExistentId = Guid.NewGuid();

        var response = await Client.GetAsync($"/api/products/{nonExistentId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
