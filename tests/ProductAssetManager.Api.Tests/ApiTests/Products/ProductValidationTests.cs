using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Products;

[TestFixture]
public class ProductValidationTests : ApiTestBase
{
    private Guid _terminalCategoryId;

    [SetUp]
    public async Task AuthorizeAsAdminAndCreateTerminalCategory()
    {
        var token = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var categoryResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();
        _terminalCategoryId = category!.Id;
    }

    [Test]
    public async Task When_CreatingProductWithValidData_Should_ReturnCreated()
    {
        var request = new CreateProductRequest
        {
            Name = "Floral Summer Dress",
            Description = "A light summer dress",
            BasePrice = 49.99m,
            Material = "Cotton",
            CategoryId = _terminalCategoryId,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "Small/Red", Sku = "DRESS-SM-RED", Quantity = 10 }
            }
        };

        var response = await Client.PostAsJsonAsync("/api/products", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var body = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Name, Is.EqualTo("Floral Summer Dress"));
        Assert.That(body.Variants, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task When_CreatingProductWithNegativeBasePrice_Should_ReturnBadRequest()
    {
        var request = new CreateProductRequest
        {
            Name = "Underpriced Dress",
            Description = "Should be rejected",
            BasePrice = -10m,
            Material = "Cotton",
            CategoryId = _terminalCategoryId,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "Only", Sku = "NEG-PRICE-SKU", Quantity = 1 }
            }
        };

        var response = await Client.PostAsJsonAsync("/api/products", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task When_CreatingProductWithMissingRequiredField_Should_ReturnBadRequest()
    {
        var request = new CreateProductRequest
        {
            Name = string.Empty,
            Description = "Missing a name",
            BasePrice = 19.99m,
            Material = "Cotton",
            CategoryId = _terminalCategoryId,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "Only", Sku = "MISSING-NAME-SKU", Quantity = 1 }
            }
        };

        var response = await Client.PostAsJsonAsync("/api/products", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
