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
    }
}
