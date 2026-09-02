using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Tests.ApiTests;

namespace ProductAssetManager.Api.Tests.ApiTests.Products;

[TestFixture]
public class ProductSearchTests : ApiTestBase
{
    [SetUp]
    public async Task SeedKnownProducts()
    {
        var token = await CreateAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var categoryResponse = await Client.PostAsJsonAsync(
            "/api/categories",
            new CreateCategoryRequest { Name = "Dresses" });
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        var seedProducts = new[]
        {
            new { Name = "Floral Summer Dress", Price = 30m, Sku = "SEARCH-FLORAL" },
            new { Name = "Casual Cotton Dress", Price = 20m, Sku = "SEARCH-CASUAL" },
            new { Name = "Elegant Evening Gown", Price = 150m, Sku = "SEARCH-GOWN" },
            new { Name = "Denim Jacket", Price = 60m, Sku = "SEARCH-JACKET" }
        };

        foreach (var seed in seedProducts)
        {
            await Client.PostAsJsonAsync("/api/products", new CreateProductRequest
            {
                Name = seed.Name,
                Description = "Seed data for search tests",
                BasePrice = seed.Price,
                Material = "Cotton",
                CategoryId = category!.Id,
                Variants = new List<CreateVariantRequest>
                {
                    new() { Name = "One Size", Sku = seed.Sku, Quantity = 10 }
                }
            });
        }

        Client.DefaultRequestHeaders.Authorization = null;
    }

    [Test]
    public async Task When_SearchingByKeyword_Should_ReturnOnlyPartialCaseInsensitiveNameMatches()
    {
        var response = await Client.GetFromJsonAsync<List<PublicProductResponse>>("/api/products?keyword=dress");

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Select(p => p.Name), Is.EquivalentTo(new[] { "Floral Summer Dress", "Casual Cotton Dress" }));
    }

    [Test]
    public async Task When_FilteringByMaxPrice_Should_ExcludeProductsAboveThatPrice()
    {
        var response = await Client.GetFromJsonAsync<List<PublicProductResponse>>("/api/products?maxPrice=50");

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Select(p => p.Name), Is.EquivalentTo(new[] { "Floral Summer Dress", "Casual Cotton Dress" }));
        Assert.That(response.Select(p => p.Name), Does.Not.Contain("Elegant Evening Gown"));
        Assert.That(response.Select(p => p.Name), Does.Not.Contain("Denim Jacket"));
    }

    [Test]
    public async Task When_CombiningKeywordAndMaxPrice_Should_ApplyBothFiltersTogether()
    {
        var response = await Client.GetFromJsonAsync<List<PublicProductResponse>>("/api/products?keyword=dress&maxPrice=25");

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Select(p => p.Name), Is.EquivalentTo(new[] { "Casual Cotton Dress" }));
    }
}
