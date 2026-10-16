using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Data;

public static class DemoCatalogSeeder
{
    public static readonly Guid CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ProductId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid VariantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public const string VariantSku = "DEMO-CHAIR-OAK";

    public static async Task SeedAsync(ApplicationDbContext dbContext)
    {
        if (await dbContext.Products.AnyAsync(p => p.Id == ProductId))
        {
            return;
        }

        var now = DateTime.UtcNow;

        dbContext.Categories.Add(new Category
        {
            Id = CategoryId,
            Name = "Demo Furniture"
        });

        dbContext.Products.Add(new Product
        {
            Id = ProductId,
            Name = "Demo Chair",
            Description = "A chair seeded so the containerised stack has something to show and buy.",
            BasePrice = 120.00m,
            Material = "Oak",
            CategoryId = CategoryId,
            CreatedAt = now,
            UpdatedAt = now,
            Variants =
            {
                new Variant
                {
                    Id = VariantId,
                    Name = "Oak",
                    SKU = VariantSku,
                    Quantity = 10
                }
            }
        });

        await dbContext.SaveChangesAsync();
    }
}
