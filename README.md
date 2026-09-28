# Product Asset Manager — Backend

ASP.NET Core Web API backend for an e-commerce inventory and order management platform (Backend Capstone,
extended with Azure Service Bus).

It covers the full lifecycle an admin-run storefront needs: a category tree, products with SKU-level
variants and stock, curated product collections, and an asynchronous purchase flow that turns stock into
orders — all behind JWT-authenticated, role-gated endpoints.

## Overview

Two kinds of accounts use this API:

- An **Admin** manages the catalog — categories, products, variants, stock levels, and collections.
- A **User** browses the public catalog and buys variants. Buying returns immediately with an order
  reference; the order is then confirmed or rejected in the background, and the shopper checks the
  outcome with `GET /api/orders/{id}`.

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

### The purchase flow (Azure Service Bus)

```
POST /api/orders                                      background, same process as the API
  │                                                    ┌──────────────────────────────────┐
  ├─ validate: quantity ≥ 1, variant exists/active     │ OrderConsumer (BackgroundService)│
  ├─ save Order as Pending  ───────────────┐           │  one session per SKU, one message│
  ├─ publish OrderPlaced ──▶ [orders queue]├──────────▶│  at a time, in order:            │
  └─ 202 Accepted { orderId }              │           │  • load Order + Variant          │
                                           │           │  • skip if not Pending           │
GET /api/orders/{id}  ◀── reads the row ───┘           │  • enough stock? decrement and   │
   Pending → Confirmed | Rejected (+ reason)           │    Confirm, else Reject + reason │
                                                       │  • one SaveChanges               │
                                                       │  • publish StockDecremented ─────┼──▶ [stock-events queue]
                                                       │  • complete the message          │
                                                       └──────────────────────────────────┘
```

- **Producer** — `OrderService.CreateAsync`. It checks only that the quantity is at least 1 and the
  variant exists and is active. It deliberately does **not** check stock: that decision belongs to the
  consumer, which is the only place that can make it safely.
- **Consumer** — `OrderConsumer`, a hosted service that starts with the API. It uses a session-aware
  processor in peek-lock mode with auto-complete off.
- **Messages** — `OrderPlaced` (`MessageId` = order id, `SessionId` = the variant's SKU, body
  `{ "OrderId": "..." }`) and `StockDecremented` (published to a second queue after a confirmed order).

| Endpoint | Auth | Result |
|---|---|---|
| `POST /api/orders` `{ variantId, quantity }` | any signed-in account | `202` with `{ orderId }`; `400` bad quantity; `404` unknown variant |
| `GET /api/orders/{id}` | the order's owner, or an Admin | `200` with status (`Pending`/`Confirmed`/`Rejected`) and the rejection reason if any; `403` someone else's order; `404` unknown id |

## Entities & Relationships

| Entity | Relationships | Notes |
|---|---|---|
| `ApplicationUser` | has many `Order` | extends ASP.NET Core Identity's user; carries `Admin`/`User` role |
| `Category` | self-referencing (`ParentCategory` / `ChildCategories`); has many `Product` | only a *terminal* category (no children) may hold products |
| `Product` | belongs to one `Category`; has many `Variant`; many-to-many with `Collection` | |
| `Variant` | belongs to one `Product`; has many `Order` | unique `SKU`; `Quantity` is the stock count; `RowVersion` is a concurrency token |
| `Order` | belongs to one `ApplicationUser`; references one `Variant` | a purchase record — snapshots `UnitPriceAtPurchase` when the order is placed; `Status` is `Pending`, `Confirmed` or `Rejected`, with a `RejectionReason` when rejected |
| `Collection` | many-to-many with `Product` via `ProductCollection` | a curated grouping (e.g. "Summer Sale") |
| `ProductCollection` | join entity, composite key `(ProductId, CollectionId)` | pure many-to-many join, no meaning of its own |

## Getting Started

### Prerequisites

- .NET SDK 10.0
- SQL Server LocalDB
- The `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef` (skip if already installed)
- An **Azure Service Bus namespace** — see [Azure Service Bus setup](#2-azure-service-bus-setup) below.
  The API stops at startup if its Service Bus connection strings are not configured.

### 1. Clone and restore

```
git clone https://github.com/Dushimepaulette1/product-asset-manager-backend.git
cd product-asset-manager-backend
dotnet restore
```

### 2. Azure Service Bus setup

The purchase flow needs a real Service Bus namespace. In the Azure Portal:

1. Create a **Service Bus namespace on the Standard tier**. The Basic tier does not support sessions,
   which the ordering guarantee depends on. Namespace names are globally unique, so pick your own.
2. Inside it, create the queue **`orders`** with **Enable sessions** turned **on**. This can only be set
   when the queue is created.
3. Create a second queue, **`stock-events`**, with sessions **off**.
4. Create a shared access policy on each queue (Queue → *Shared access policies* → *Add*):
   - on `orders`: **Send** and **Listen**
   - on `stock-events`: **Send**

Nothing about the queues is committed to the repository: not the two connection strings, and not the
queue names either. All four are read from configuration (`ServiceBus:ConnectionString`,
`ServiceBus:StockEventsConnectionString`, `ServiceBus:QueueName`, `ServiceBus:StockEventsQueueName`) and
you set them in the next step. Use whatever queue names you created; the commands below assume `orders`
and `stock-events`.

A Standard namespace has a fixed monthly charge whether or not it is used, so delete it when you have
finished.

### 3. Configure local secrets

Six values are intentionally **never committed** to source control — a JWT signing key, the seeded
Admin account's password, the two Service Bus connection strings, and the two queue names. Set them
once via `dotnet user-secrets`, from the `src/` folder:

```
cd src
dotnet user-secrets set "Jwt:SigningKey" "CapstoneDemo-2026-LocalOnly-SigningKey-ChangeMe"
dotnet user-secrets set "SeedAdmin:Password" "AdminDemo123!"
dotnet user-secrets set "ServiceBus:ConnectionString" "<connection string of the orders queue policy>"
dotnet user-secrets set "ServiceBus:StockEventsConnectionString" "<connection string of the stock-events queue policy>"
dotnet user-secrets set "ServiceBus:QueueName" "orders"
dotnet user-secrets set "ServiceBus:StockEventsQueueName" "stock-events"
```

Any sufficiently long random string works for the signing key — this app only ever validates tokens it
signed itself, so there's no shared secret to obtain from anyone. The values above are the ones this
README's demo credentials (below) assume; feel free to set your own instead if you don't need the demo
login to work.

If a Service Bus value is missing, the app stops at startup with a message naming the missing key
(for example `ServiceBus:QueueName is not configured.`) instead of failing later on the first
purchase.

### 4. Apply migrations and run

```
dotnet ef database update
dotnet run
```

On first startup (Development environment only) the app also seeds the `Admin`/`User` roles and one
Admin account, using `SeedAdmin:Email` (`appsettings.Development.json`, already `admin@local.dev`) and
the `SeedAdmin:Password` secret set above. The order consumer starts with the API — there is nothing
separate to run.

The API listens at `http://localhost:5260` by default (see `src/Properties/launchSettings.json`).
Swagger UI is available at `http://localhost:5260/swagger` — use it to authorize with a bearer token
and try any endpoint interactively.

### Try the purchase flow

In Swagger (or any HTTP client), in order:

1. `POST /api/auth/login` as the Admin (see [Demo Credentials](#demo-credentials)) and authorize with the token.
2. `POST /api/categories`, then `POST /api/products` with one variant, e.g. `"sku": "DEMO-SKU", "quantity": 3`.
   Copy the variant's `id` from the response.
3. `POST /api/auth/register` a User, `POST /api/auth/login` as them, and authorize with *that* token.
4. `POST /api/orders` with `{ "variantId": "<id>", "quantity": 2 }` → **`202 Accepted`** with an `orderId`.
5. `GET /api/orders/{orderId}` → `Confirmed` (usually already, within a second or two). The variant's stock
   is now 1.
6. Buy `quantity: 5` more → still `202` (the endpoint never checks stock), then `GET` it → **`Rejected`**
   with a reason such as `Only 1 unit(s) of 'X' were available.`, and the stock is unchanged.

To see the message itself, open the `orders` queue in the Portal → *Service Bus Explorer* → *Peek*.
While the API is running the consumer takes messages off almost immediately, so stop the app first if you
want one to stay visible.

### Running the tests

```
dotnet test
```

Tests are NUnit, split into two kinds:

- **Service tests** (`tests/.../ServiceTests`) exercise a service directly against a real, disposable
  LocalDB database — no mocking anywhere in this project.
- **API tests** (`tests/.../ApiTests`) exercise real HTTP calls through `WebApplicationFactory<Program>`
  against a second, separately disposable LocalDB database.

Both kinds create and drop their own database per test run, and neither touches the
`ProductAssetManagerDb` database `dotnet run` uses.

The API tests boot the real application, so they need the Service Bus secrets from step 3, and every test
that places an order talks to your **real** namespace — nothing is mocked. Service tests need only
LocalDB. The async tests are the ones to read:

- `AsyncPurchaseTests` — buy, get `202`, poll `GET /api/orders/{id}` until `Confirmed`, stock reduced.
- `LoadBufferingTests` — 20 concurrent purchases against 6 units of stock: exactly 6 `Confirmed`, 14
  `Rejected`, final stock exactly 0.
- `ConsumerRecoveryTests` — stop the consumer, place orders (they stay `Pending`), restart it: every order
  resolves and stock drops by exactly one unit per order.

In tests the consumer does not start on its own (`ServiceBus:AutoStartConsumer` is `false` in the test
host), so tests that don't want it running stay deterministic; the async tests above start it
explicitly. Tests that place an order without starting the consumer leave their `OrderPlaced` message in
the queue. That is harmless — a later consumer completes and discards a message whose order does not exist
in its database — but it means the queue slowly collects test messages unless you purge it.

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

**Purchasing is asynchronous, and the queue — not the database — is what prevents overselling.** The
original synchronous purchase guarded the last unit with an atomic conditional `UPDATE`. That was replaced
when the flow moved to Service Bus. Every `OrderPlaced` message carries the variant's SKU as its
`SessionId`, and the consumer's processor runs with `MaxConcurrentCallsPerSession = 1`. Service Bus then
hands out one session to one receiver at a time and delivers its messages in order, so purchases of the
same variant are processed strictly one after another and the earliest buyers get the last units. Different
SKUs are still processed in parallel (up to five sessions at once).

**Sessions do not make the concurrency token redundant.** Sessions only serialise purchases against *other
purchases*. An Admin editing stock through `PATCH /api/variants/{sku}/stock` is a completely separate code
path that never touches the queue, and could write the same row at the same moment. `Variant.RowVersion`
(a SQL Server `rowversion`) is the guard for that: if the row changed between the consumer reading it and
saving, EF Core throws `DbUpdateConcurrencyException`, and the consumer reloads the fresh values and
re-decides — up to three attempts — rather than overwriting the Admin's change. Because real contention is
now rare (admin against consumer, no longer buyer against buyer), an optimistic retry is the right size of
tool here.

**A message is only completed after everything it triggered has succeeded.** The consumer runs in
peek-lock mode with auto-complete off. It completes the message last: after the single `SaveChanges()`
that confirms or rejects the order, and after the `StockDecremented` event is published. If anything before
that throws, the message is left uncompleted, so the lock expires and Service Bus redelivers it — the order
is not lost. The exception is logged by the processor's error handler and does not stop the consumer.

**Redelivery cannot double-process an order.** Service Bus delivers at least once, so a message can arrive
again after it was already handled (for example if the process dies after the database save but before the
message is completed). The consumer therefore checks that the order is still `Pending` before doing
anything; if it is not, it completes the message and stops. A redelivered message for a confirmed order
leaves stock untouched.

**The producer never leaves a `Pending` order that nothing will process.** The order is saved before the
message is sent. If sending fails, the just-created order is deleted and the error is rethrown, so the
caller gets an honest error rather than an order that looks accepted and never resolves.

**The price is fixed when the order is placed, not when it is processed.** `UnitPriceAtPurchase` is set
by the producer from the variant's price (or the product's base price), so a price change between the click
and the background processing does not affect an order the shopper already placed.

**The message body is just the order id.** `MessageId` already carries it (which is also what
the queue's optional duplicate detection keys on), and the consumer's first step is loading the order
anyway, so `VariantId` and `Quantity` are read from the database rather than copied into the message
where they could drift from it.

**Two events, one consumer.** `OrderPlaced` is consumed by this application. `StockDecremented` is
published to a separate `stock-events` queue after a confirmed order commits, so something else — a
low-stock alert, a dashboard — could react to inventory changes without knowing anything about checkout.
Nothing in this project consumes it yet.

**The `Order.Status` enum is ordered `Confirmed, Pending, Rejected`, on purpose.** C# defaults an unset enum
to its first member, and EF Core treats a property equal to its CLR default as "unset" and lets a database
default take over. With `Pending` first, explicitly saving a new order as `Pending` would have been silently
replaced by the database default. Putting `Confirmed` first avoids that; existing orders, which all
succeeded under the old synchronous flow, were backfilled to `Confirmed` by the migration. Enums are
serialised in API responses as text (`"Pending"`), not numbers.

**`POST /api/orders` changed contract.** It used to return `201` with the full order after reducing stock.
It now returns `202` with only an `orderId`, because the outcome is not known yet. Clients must read
`GET /api/orders/{id}` for the result.

## Known Limitations

- **Checkout still needs the database.** The producer saves the `Pending` order before publishing, so the
  queue protects the stock update, not the order insert: a database that is slow makes checkout slower,
  and one that is down makes it fail.
- **A message that keeps failing is eventually dead-lettered.** After the queue's maximum delivery count
  (a queue setting; Service Bus defaults it to 10) the message moves to the dead-letter queue, where nothing
  in this project reads it, so that order stays `Pending`.
- **`StockDecremented` has no subscriber** — it is published for future consumers.
- **The order-placing tests need a real Service Bus namespace**, and leave messages behind, as described
  under *Running the tests*.
