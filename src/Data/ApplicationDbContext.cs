using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProductAssetManager.Api.Models;

namespace ProductAssetManager.Api.Data;

// Layering note: Controllers stay thin (HTTP concerns, status codes, route authorization only).
// Services hold business logic (validation, transactions, stock calculations) and are the layer
// service-level xUnit tests target directly. Services take this ApplicationDbContext directly
// rather than going through a separate repository layer - for this capstone's scope that's a
// reasonable, idiomatic simplification, not a shortcut.
//
// Extends IdentityDbContext<ApplicationUser> so ASP.NET Core Identity's own tables (users,
// roles, role membership, etc.) are created and tied to our ApplicationUser automatically.
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
        // Must run first - this is what configures Identity's own tables. Skipping it
        // silently breaks login/roles.
        base.OnModelCreating(builder);

        builder.Entity<Category>(entity =>
        {
            // Self-referencing hierarchy. Restrict, not Cascade: SQL Server rejects cascade
            // on self-referencing FKs outright (risk of infinite cascade loops), so this is
            // effectively mandatory here, not just a safety choice.
            entity.HasOne(c => c.ParentCategory)
                .WithMany(c => c.ChildCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Sibling-name uniqueness needs two filtered indexes, not one plain index on
            // (ParentCategoryId, Name): SQL Server treats every NULL as distinct from every
            // other NULL, so a plain unique index would let multiple root-level categories
            // (ParentCategoryId IS NULL) share the same Name without being caught.
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
            // Restrict: deleting a category that still has products assigned to it is
            // refused by the database rather than silently deleting those products.
            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            // SQL Server needs an explicit precision/scale for decimal columns, or EF Core
            // falls back to a default that can silently round money values.
            entity.Property(p => p.BasePrice).HasPrecision(18, 2);
        });

        builder.Entity<Variant>(entity =>
        {
            // Required, Restrict: a Variant can never exist without its parent Product, and
            // a Product can't be deleted while it still has variants - the capstone's explicit
            // no-orphan-variant requirement, enforced by the database itself.
            entity.HasOne(v => v.Product)
                .WithMany(p => p.Variants)
                .HasForeignKey(v => v.ProductId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            // Global uniqueness across the whole platform, not just within one product.
            entity.HasIndex(v => v.SKU).IsUnique();

            entity.Property(v => v.Price).HasPrecision(18, 2);
        });
    }
}
