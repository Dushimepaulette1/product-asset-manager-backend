using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Services;
using ProductAssetManager.Api.Tests.ServiceTests;

namespace ProductAssetManager.Api.Tests.ServiceTests.Products;

[TestFixture]
public class ProductServiceTests : ServiceTestBase
{
    private async Task<Category> CreateTerminalCategoryAsync(string name = "Dresses")
    {
        var category = new Category { Name = name };
        DbContext.Categories.Add(category);
        await DbContext.SaveChangesAsync();
        return category;
    }

    private ProductService CreateProductService() => new(DbContext, new CategoryService(DbContext));

    [Test]
    public async Task When_CreatingProductWithValidVariants_Should_PersistProductAndVariants()
    {
        var category = await CreateTerminalCategoryAsync();
        var productService = CreateProductService();

        var request = new CreateProductRequest
        {
            Name = "Floral Summer Dress",
            Description = "A light summer dress",
            BasePrice = 49.99m,
            Material = "Cotton",
            CategoryId = category.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "Small/Red", Sku = "DRESS-SM-RED", Quantity = 10 },
                new() { Name = "Medium/Red", Price = 54.99m, Sku = "DRESS-MD-RED", Quantity = 3 }
            }
        };

        var result = await productService.CreateAsync(request);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Product, Is.Not.Null);
        Assert.That(result.Product!.Variants, Has.Count.EqualTo(2));

        var persistedProduct = await DbContext.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == result.Product.Id);

        Assert.That(persistedProduct, Is.Not.Null);
        Assert.That(persistedProduct!.Name, Is.EqualTo("Floral Summer Dress"));
        Assert.That(persistedProduct.Variants, Has.Count.EqualTo(2));
        Assert.That(persistedProduct.Variants.Select(v => v.SKU), Is.EquivalentTo(new[] { "DRESS-SM-RED", "DRESS-MD-RED" }));
    }

    [Test]
    public async Task When_CreatingProductWithDuplicateSkuAgainstExistingVariant_Should_PersistNothing()
    {
        var category = await CreateTerminalCategoryAsync();
        var productService = CreateProductService();

        var existingProduct = new Product
        {
            Name = "Existing Product",
            Description = "Already here",
            BasePrice = 10m,
            Material = "Cotton",
            CategoryId = category.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Variants = new List<Variant>
            {
                new() { Name = "Existing Variant", SKU = "ALREADY-TAKEN", Quantity = 5 }
            }
        };
        DbContext.Products.Add(existingProduct);
        await DbContext.SaveChangesAsync();

        var request = new CreateProductRequest
        {
            Name = "Should Not Persist",
            Description = "This entire product should be rolled back",
            BasePrice = 19.99m,
            Material = "Wool",
            CategoryId = category.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "Would Be Valid", Sku = "TOTALLY-NEW-SKU", Quantity = 5 },
                new() { Name = "Conflicts", Sku = "ALREADY-TAKEN", Quantity = 2 }
            }
        };

        var result = await productService.CreateAsync(request);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ValidationError, Is.Not.Null);

        var productCount = await DbContext.Products.CountAsync();
        Assert.That(productCount, Is.EqualTo(1), "only the pre-existing product should exist");

        var shouldNotExist = await DbContext.Products.AnyAsync(p => p.Name == "Should Not Persist");
        Assert.That(shouldNotExist, Is.False);

        var newSkuShouldNotExist = await DbContext.Variants.AnyAsync(v => v.SKU == "TOTALLY-NEW-SKU");
        Assert.That(newSkuShouldNotExist, Is.False, "the otherwise-valid variant must not survive the rollback either");

        var variantCount = await DbContext.Variants.CountAsync();
        Assert.That(variantCount, Is.EqualTo(1), "only the pre-existing variant should exist");
    }

    [Test]
    public async Task When_CreatingProductWithDuplicateSkuWithinSameRequest_Should_PersistNothing()
    {
        var category = await CreateTerminalCategoryAsync();
        var productService = CreateProductService();

        var request = new CreateProductRequest
        {
            Name = "Should Also Not Persist",
            Description = "In-request duplicate",
            BasePrice = 19.99m,
            Material = "Wool",
            CategoryId = category.Id,
            Variants = new List<CreateVariantRequest>
            {
                new() { Name = "A", Sku = "DUPE-IN-REQUEST", Quantity = 5 },
                new() { Name = "B", Sku = "DUPE-IN-REQUEST", Quantity = 2 }
            }
        };

        var result = await productService.CreateAsync(request);

        Assert.That(result.Succeeded, Is.False);

        var productCount = await DbContext.Products.CountAsync();
        Assert.That(productCount, Is.EqualTo(0));

        var variantCount = await DbContext.Variants.CountAsync();
        Assert.That(variantCount, Is.EqualTo(0));
    }
}
