using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Variant> Variants => Set<Variant>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<ProductCollection> ProductCollections => Set<ProductCollection>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Category>(entity =>
        {
            entity.HasOne(c => c.ParentCategory)
                .WithMany(c => c.ChildCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(c => c.Name)
                .IsUnique()
                .HasFilter("[ParentCategoryId] IS NULL")
                .HasDatabaseName("IX_Categories_Name_RootLevel");

            entity.HasIndex(c => new { c.ParentCategoryId, c.Name })
                .IsUnique()
                .HasFilter("[ParentCategoryId] IS NOT NULL")
                .HasDatabaseName("IX_Categories_ParentCategoryId_Name");
        });

        builder.Entity<Product>(entity =>
        {
            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(p => p.BasePrice).HasPrecision(18, 2);
        });

        builder.Entity<Variant>(entity =>
        {
            entity.HasOne(v => v.Product)
                .WithMany(p => p.Variants)
                .HasForeignKey(v => v.ProductId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(v => v.SKU).IsUnique();

            entity.Property(v => v.Price).HasPrecision(18, 2);
        });

        builder.Entity<ProductCollection>(entity =>
        {
            entity.HasKey(pc => new { pc.ProductId, pc.CollectionId });

            entity.HasOne(pc => pc.Product)
                .WithMany(p => p.ProductCollections)
                .HasForeignKey(pc => pc.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(pc => pc.Collection)
                .WithMany(c => c.ProductCollections)
                .HasForeignKey(pc => pc.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Order>(entity =>
        {
            entity.HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.Variant)
                .WithMany(v => v.Orders)
                .HasForeignKey(o => o.VariantId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(o => o.UnitPriceAtPurchase).HasPrecision(18, 2);
        });
    }
}
