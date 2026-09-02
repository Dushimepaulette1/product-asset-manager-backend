using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Services;
using ProductAssetManager.Api.Tests.ServiceTests;

namespace ProductAssetManager.Api.Tests.ServiceTests.Products;

[TestFixture]
public class ProductStockStatusTests : ServiceTestBase
{
    [Test]
    public async Task When_VariantsHaveBoundaryQuantities_Should_MapToCorrectStockStatus()
    {
        var category = new Category { Name = "Dresses" };
        DbContext.Categories.Add(category);

        var product = new Product
        {
            Name = "Boundary Test Dress",
            Description = "Tests stock status boundaries",
            BasePrice = 29.99m,
            Material = "Cotton",
            CategoryId = category.Id,
            Category = category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Variants = new List<Variant>
            {
                new() { Name = "Out of stock", SKU = "BOUNDARY-ZERO", Quantity = 0 },
                new() { Name = "Low stock", SKU = "BOUNDARY-FOUR", Quantity = 4 },
                new() { Name = "In stock", SKU = "BOUNDARY-FIVE", Quantity = 5 }
            }
        };
        DbContext.Products.Add(product);
        await DbContext.SaveChangesAsync();

        var productService = new ProductService(DbContext, new CategoryService(DbContext));

        var response = await productService.GetByIdAsync(product.Id);

        Assert.That(response, Is.Not.Null);

        var bySku = response!.Variants.ToDictionary(v => v.Sku, v => v.StockStatus);

        Assert.That(bySku["BOUNDARY-ZERO"], Is.EqualTo("OUT_OF_STOCK"));
        Assert.That(bySku["BOUNDARY-FOUR"], Is.EqualTo("LOW_STOCK"));
        Assert.That(bySku["BOUNDARY-FIVE"], Is.EqualTo("IN_STOCK"));
    }
}
