using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Products;

[TestFixture]
public class ProductUpdateTests : ApiTestBase
{
    private Guid _productId;
    private Guid _nonTerminalCategoryId;

    [SetUp]
    public async Task AuthorizeAsAdminAndSeedProduct()
    {
        var token = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var parentResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Clothing" });
        var parent = await parentResponse.Content.ReadFromJsonAsync<CategoryResponse>();
        _nonTerminalCategoryId = parent!.Id;

        var childResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses", ParentCategoryId = parent.Id });
        var child = await childResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var productResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Update Test Dress",
            Description = "Original description",
            BasePrice = 29.99m,
            Material = "Cotton",
            CategoryId = child!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "One Size", Sku = "UPDATE-TEST-SKU", Quantity = 10 }
            }
        });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponse>();
        _productId = product!.Id;
    }

    [Test]
    public async Task When_UpdatingWithOnlyBasePrice_Should_UpdateOnlyThatFieldAndReturnOk()
    {
        var response = await Client.PatchAsJsonAsync(
            $"/api/products/{_productId}",
            new UpdateProductRequest { BasePrice = 39.99m });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.That(body!.BasePrice, Is.EqualTo(39.99m));
        Assert.That(body.Description, Is.EqualTo("Original description"));
        Assert.That(body.Variants, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task When_UpdatingWithNegativePrice_Should_ReturnBadRequest()
    {
        var response = await Client.PatchAsJsonAsync(
            $"/api/products/{_productId}",
            new UpdateProductRequest { BasePrice = -10m });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task When_ReassigningToNonTerminalCategory_Should_ReturnBadRequest()
    {
        var response = await Client.PatchAsJsonAsync(
            $"/api/products/{_productId}",
            new UpdateProductRequest { CategoryId = _nonTerminalCategoryId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task When_UpdatingNonExistentProduct_Should_ReturnNotFound()
    {
        var response = await Client.PatchAsJsonAsync(
            $"/api/products/{Guid.NewGuid()}",
            new UpdateProductRequest { BasePrice = 10m });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
