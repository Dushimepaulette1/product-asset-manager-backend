using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.DTOs;
using ProductAssetManager.Api.Models;
using ProductAssetManager.Api.Services;
using ProductAssetManager.Api.Tests.ServiceTests;

namespace ProductAssetManager.Api.Tests.ServiceTests.Products;

[TestFixture]
public class AddVariantTests : ServiceTestBase
{
    private async Task<Product> CreateProductAsync()
    {
        var category = new Category { Name = "Dresses" };
        DbContext.Categories.Add(category);

        var product = new Product
        {
            Name = "Floral Summer Dress",
            Description = "A light summer dress",
            BasePrice = 49.99m,
            Material = "Cotton",
            CategoryId = category.Id,
            Category = category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Variants = new List<Variant>
            {
                new() { Name = "Small/Red", SKU = "EXISTING-SKU", Quantity = 10 }
            }
        };
        DbContext.Products.Add(product);
        await DbContext.SaveChangesAsync();

        return product;
    }

    private ProductService CreateProductService() => new(DbContext, new CategoryService(DbContext));

    [Test]
    public async Task When_AddingValidVariantToExistingProduct_Should_PersistIt()
    {
        var product = await CreateProductAsync();
        var productService = CreateProductService();

        var result = await productService.AddVariantAsync(product.Id, new CreateVariantRequest
        {
            Name = "Medium/Red",
            Sku = "NEW-VARIANT-SKU",
            Quantity = 8
        });

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Variant, Is.Not.Null);
        Assert.That(result.Variant!.Sku, Is.EqualTo("NEW-VARIANT-SKU"));

        var persistedVariant = await DbContext.Variants.FirstOrDefaultAsync(v => v.SKU == "NEW-VARIANT-SKU");
        Assert.That(persistedVariant, Is.Not.Null);
        Assert.That(persistedVariant!.ProductId, Is.EqualTo(product.Id));

        var variantCount = await DbContext.Variants.CountAsync(v => v.ProductId == product.Id);
        Assert.That(variantCount, Is.EqualTo(2));
    }

    [Test]
    public async Task When_AddingVariantWithDuplicateSku_Should_RejectAndPersistNothing()
    {
        var product = await CreateProductAsync();
        var productService = CreateProductService();

        var result = await productService.AddVariantAsync(product.Id, new CreateVariantRequest
        {
            Name = "Conflicts",
            Sku = "EXISTING-SKU",
            Quantity = 3
        });

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ValidationError, Is.Not.Null);

        var variantCount = await DbContext.Variants.CountAsync(v => v.ProductId == product.Id);
        Assert.That(variantCount, Is.EqualTo(1), "no new variant should have been added");
    }

    [Test]
    public async Task When_AddingVariantToNonExistentProduct_Should_ReturnProductNotFound()
    {
        var productService = CreateProductService();

        var result = await productService.AddVariantAsync(Guid.NewGuid(), new CreateVariantRequest
        {
            Name = "Ghost",
            Sku = "GHOST-SKU",
            Quantity = 1
        });

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ProductNotFound, Is.True);
    }

    [Test]
    public async Task When_AddingVariantWithNegativeQuantity_Should_ReturnValidationError()
    {
        var product = await CreateProductAsync();
        var productService = CreateProductService();

        var result = await productService.AddVariantAsync(product.Id, new CreateVariantRequest
        {
            Name = "Bad Quantity",
            Sku = "NEG-QTY-SKU",
            Quantity = -1
        });

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.ValidationError, Is.Not.Null);

        var exists = await DbContext.Variants.AnyAsync(v => v.SKU == "NEG-QTY-SKU");
        Assert.That(exists, Is.False);
    }
}
