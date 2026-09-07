# Product Asset Manager — Backend

ASP.NET Core Web API backend for an e-commerce inventory and order management platform (Backend Capstone).

It covers the full lifecycle an admin-run storefront needs: a category tree, products with SKU-level
variants and stock, curated product collections, and a purchase flow that turns stock into orders —
all behind JWT-authenticated, role-gated endpoints.

## Overview

Two kinds of accounts use this API:

- An **Admin** manages the catalog — categories, products, variants, stock levels, and collections.
- A **User** browses the public catalog and purchases variants, which creates an order and reduces stock.

## Architecture

```
Controller  →  Service  →  ApplicationDbContext (EF Core)  →  SQL Server
```

- **Controllers** are thin. They call one service method and translate its result into an HTTP status
  code — they contain no business logic of their own.
- **Services** hold all business logic and validation, and are injected with `ApplicationDbContext`
  directly. There is no repository layer — with EF Core already providing a unit-of-work and testable
  abstraction over the database, an extra repository layer would just be indirection with no real
  benefit here.
- **Result objects**, not exceptions, communicate expected failures. Each service method that can fail
  returns a typed record (e.g. `CreateProductResult(bool Succeeded, bool CategoryNotFound, string?
  ValidationError, ProductResponse? Product)`), and the controller reads its flags to decide which HTTP
  status to return. Exceptions stay reserved for genuinely unexpected failures.
- **Shared static mappers** (`ProductMapper`, `VariantMapper`) hold the one place each entity is
  converted to its DTO shape, so services that need the same shape don't drift from each other.
- **Global exception-handling middleware** (`GlobalExceptionHandler`) is a last-resort safety net —
  any exception that isn't one of the above expected, handled cases still returns a clean, structured
  500 instead of leaking a raw stack trace to the client.
- **Authentication** is JWT bearer tokens issued by ASP.NET Core Identity, carrying an `Admin` or `User`
  role claim that `[Authorize(Roles = "Admin")]` (or a bare `[Authorize]` for "any signed-in account")
  checks on every protected endpoint.

## Entities & Relationships

| Entity | Relationships | Notes |
|---|---|---|
| `ApplicationUser` | has many `Order` | extends ASP.NET Core Identity's user; carries `Admin`/`User` role |
| `Category` | self-referencing (`ParentCategory` / `ChildCategories`); has many `Product` | only a *terminal* category (no children) may hold products |
| `Product` | belongs to one `Category`; has many `Variant`; many-to-many with `Collection` | |
| `Variant` | belongs to one `Product`; has many `Order` | unique `SKU`; `Quantity` is the stock count |
| `Order` | belongs to one `ApplicationUser`; references one `Variant` | a purchase record — snapshots `UnitPriceAtPurchase` so later price changes never rewrite history |
| `Collection` | many-to-many with `Product` via `ProductCollection` | a curated grouping (e.g. "Summer Sale") |
| `ProductCollection` | join entity, composite key `(ProductId, CollectionId)` | pure many-to-many join, no meaning of its own |

## Getting Started

### Prerequisites

- .NET SDK 10.0
- SQL Server LocalDB
- The `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef` (skip if already installed)

### 1. Clone and restore

```
git clone https://github.com/Dushimepaulette1/product-asset-manager-backend.git
cd product-asset-manager-backend
dotnet restore
```

### 2. Configure local secrets

Two values are intentionally **never committed** to source control — a JWT signing key and the seeded
Admin account's password — set them once via `dotnet user-secrets`, from the `src/` folder:

```
cd src
dotnet user-secrets set "Jwt:SigningKey" "CapstoneDemo-2026-LocalOnly-SigningKey-ChangeMe"
dotnet user-secrets set "SeedAdmin:Password" "AdminDemo123!"
```

Any sufficiently long random string works for the signing key — this app only ever validates tokens it
signed itself, so there's no shared secret to obtain from anyone. The values above are the ones this
README's demo credentials (below) assume; feel free to set your own instead if you don't need the demo
login to work.

### 3. Apply migrations and run

```
dotnet ef database update
dotnet run
```

On first startup (Development environment only) the app also seeds the `Admin`/`User` roles and one
Admin account, using `SeedAdmin:Email` (`appsettings.Development.json`, already `admin@local.dev`) and
the `SeedAdmin:Password` secret set above.

The API listens at `http://localhost:5260` by default (see `src/Properties/launchSettings.json`).
Swagger UI is available at `http://localhost:5260/swagger` — use it to authorize with a bearer token
and try any endpoint interactively.

### Running the tests

```
dotnet test
```

Tests are NUnit, split into two kinds:

- **Service tests** (`tests/.../ServiceTests`) exercise a service directly against a real, disposable
  LocalDB database — no mocking anywhere in this project.
- **API tests** (`tests/.../ApiTests`) exercise real HTTP calls through `WebApplicationFactory<Program>`
  against a second, separately disposable LocalDB database.

Both kinds create and drop their own database per test run, so `dotnet test` needs no setup beyond
LocalDB already being installed — it does not touch the `ProductAssetManagerDb` database `dotnet run`
uses.

## Demo Credentials

**Admin** (seeded automatically on first `dotnet run`, once the secrets above are set):

| | |
|---|---|
| Email | `admin@local.dev` |
| Password | `AdminDemo123!` |

Log in via `POST /api/auth/login` to get a bearer token, then use it to reach every Admin-only endpoint
(creating categories/products/variants/collections, updating stock, etc.).

**User accounts** are not seeded — create one via `POST /api/auth/register` with any email and a
password meeting the policy (6+ characters, at least one uppercase, one lowercase, one digit, one
non-alphanumeric character), then log in the same way. A registered account is automatically given the
`User` role, which is enough to browse the public catalog and purchase via `POST /api/orders`.

## Design Decisions

A few choices made along the way that aren't obvious just from reading the code:

**Every foreign key uses `Restrict`, not `Cascade`, delete behavior.** EF Core's default cascade
behavior on a required relationship would let deleting a `Category`, for instance, silently delete every
`Product` beneath it as a side effect. `Restrict` forces that to be handled explicitly instead of
happening invisibly. The one deliberate exception is `ProductCollection`, which does cascade — it's a
pure join table with no meaning of its own, so cleaning up its rows when either side is deleted is
exactly the right behavior.

**Category name uniqueness needed two separate indexes, not one.** A single unique index on
`(ParentCategoryId, Name)` looks sufficient for "no duplicate names under the same parent" — until you
consider root categories, where `ParentCategoryId` is `NULL` for all of them. SQL Server treats every
`NULL` in a unique index as distinct from every other `NULL`, so that index alone would silently allow
unlimited root categories all named "Dresses". Fixed with two filtered unique indexes instead: one on
`Name` where `ParentCategoryId IS NULL` (catches root-level duplicates), and the original one restricted
to `WHERE ParentCategoryId IS NOT NULL` (catches duplicates under the same parent) — together they cover
both cases without conflicting with each other.

**Product creation is one `SaveChangesAsync()` call, not an explicit transaction.** A product and its
variants must be saved together or not at all — a half-saved product with no variants is invalid data.
Since both are new entities tracked by the same `DbContext` instance, adding the whole graph and calling
`SaveChangesAsync()` once gives that atomicity for free: EF Core wraps one `SaveChanges` call's inserts
in a single database transaction implicitly, so there's no need to manage one by hand here.

**The purchase endpoint needs that same atomicity — a new `Order` and the corresponding stock decrease
must succeed or fail together.** Unlike product creation, the stock decrease can't just be "read the
current quantity, then write the new one": two near-simultaneous purchases for the last unit could both
read the same starting stock, both pass validation, and both succeed — overselling. The fix collapses
the check and the decrease into one atomic conditional `UPDATE` (EF Core's `ExecuteUpdateAsync` with a
`WHERE Quantity >= @qty` clause), so the check happens at the moment of the database write, not the
start of the request. The database itself serializes concurrent writers to the same row, so the losing
request's `UPDATE` naturally matches zero rows once stock is gone — no explicit locking or
optimistic-concurrency retry loop needed. Because `ExecuteUpdateAsync` bypasses the normal change
tracker (unlike the product-creation case above), it's wrapped in an explicit database transaction
together with the `Order` insert, so the two still commit or roll back as one unit.
