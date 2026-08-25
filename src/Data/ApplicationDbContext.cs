using Microsoft.EntityFrameworkCore;

namespace ProductAssetManager.Api.Data;

// Layering note: Controllers stay thin (HTTP concerns, status codes, route authorization only).
// Services hold business logic (validation, transactions, stock calculations) and are the layer
// service-level xUnit tests target directly. Services take this ApplicationDbContext directly
// rather than going through a separate repository layer - for this capstone's scope that's a
// reasonable, idiomatic simplification, not a shortcut.
//
// DbSets and entity configuration (Fluent API, keys, constraints, migrations) are added in
// Card 3 once the entity classes below are finalized.
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
}
