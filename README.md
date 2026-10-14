# Product Asset Manager — Backend

[![CI/CD](https://github.com/Dushimepaulette1/product-asset-manager-backend/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Dushimepaulette1/product-asset-manager-backend/actions/workflows/ci.yml)

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
POST /api/orders                                      background: in the API process, or its own container
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
- **Consumer** — `OrderConsumer`, a hosted service. With `dotnet run` it starts inside the API process;
  under Docker Compose it runs in its own `consumer` container (see `Api__Enabled` / `Consumer__Enabled`
  below). It uses a session-aware processor in peek-lock mode with auto-complete off.
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

## Running the whole system with Docker

This is the quickest way to see everything working. It needs **no Azure account and no .NET SDK**:
Docker runs the API, the order consumer, SQL Server and a local Service Bus emulator for you.

### Prerequisites

- **Docker Desktop** (Windows/macOS) or **Docker Engine 24+** (Linux) with **Docker Compose v2.20+**.
  Tested with Docker 29.1.3 and Compose v2.40.3. Check yours with `docker version` and `docker compose version`.
- About **6 GB of memory** available to Docker (two SQL Server containers are the heavy part).
- Free ports `8080`, `14333`, `5672` and `5300`, or change them in `.env`.

### Start and stop

```
git clone https://github.com/Dushimepaulette1/product-asset-manager-backend.git
cd product-asset-manager-backend
cp .env.example .env
docker compose up --build
```

`cp .env.example .env` creates your private settings file (it is gitignored). The placeholder values work
as they are for a local run; change the passwords if you like. The first start takes a few minutes while
images download and SQL Server initialises. The stack is ready when `api` and `consumer` show as
`(healthy)` in `docker compose ps`.

| Command | What it does |
|---|---|
| `docker compose up --build` | build the app image and start everything (add `-d` to run in the background) |
| `docker compose ps -a` | list services and their health |
| `docker compose logs -f consumer` | follow one service's logs |
| `docker compose stop consumer` / `docker compose start consumer` | stop or restart only the consumer |
| `docker compose down` | stop and remove the containers, **keeping** the database volume |
| `docker compose down --volumes` | the same, and also delete all data for a completely fresh start |

### Services

| Service | What it is | URL / port on your machine |
|---|---|---|
| `api` | the Web API (`Consumer__Enabled=false`) | `http://localhost:8080` |
| — Swagger UI | interactive API docs | `http://localhost:8080/swagger` |
| — health | liveness / readiness | `http://localhost:8080/health/live`, `http://localhost:8080/health/ready` |
| `consumer` | the order consumer (`Api__Enabled=false`); same image as `api` | not published (internal only) |
| `migrator` | one-off job: applies migrations and seeds data, then exits | — |
| `sqlserver` | the application database, data kept in the `sqlserver-data` volume | `localhost,14333` (user `sa`, password from `.env`) |
| `servicebus` | Azure Service Bus emulator with the `orders` (sessions) and `stock-events` queues | AMQP `localhost:5672`, management `http://localhost:5300` |
| `servicebus-sql` | the emulator's own SQL Server (the emulator needs one) | not published |
| `servicebus-ready` | one-off job that waits until the emulator reports healthy, then exits | — |

All ports come from `.env` (`API_PORT`, `SQL_PORT`, `SERVICEBUS_PORT`, `SERVICEBUS_MANAGEMENT_PORT`).

### Start-up order

Compose starts things in this order, using health checks, so nothing starts before what it needs:

```
sqlserver (healthy) ──▶ migrator (exits 0) ─────────┐
                                                    ├──▶ api, consumer
servicebus-sql (healthy) ──▶ servicebus ──▶ servicebus-ready (exits 0)
```

- `sqlserver` and `servicebus-sql` are healthy once `sqlcmd` can run `SELECT 1`.
- The emulator image contains no shell, so it cannot health-check itself. `servicebus-ready` (a tiny curl
  container) polls the emulator's `http://servicebus:5300/health` instead and exits once it says `healthy`.
- `api` and `consumer` report healthy once `GET /health/ready` returns `200`.

### Try it

With the stack running, in a bash shell (Git Bash or WSL on Windows):

```bash
BASE=http://localhost:8080

# 1. the demo product seeded by the migrator
curl $BASE/api/products/22222222-2222-2222-2222-222222222222

# 2. register a shopper and log in
curl -X POST $BASE/api/auth/register -H "Content-Type: application/json" \
  -d '{"name":"Shopper","email":"shopper@example.com","password":"Shopper_123!"}'
TOKEN=$(curl -s -X POST $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"shopper@example.com","password":"Shopper_123!"}' | sed -E 's/.*"token":"([^"]+)".*/\1/')

# 3. buy one demo chair -> 202 Accepted with an orderId
curl -X POST $BASE/api/orders -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"variantId":"33333333-3333-3333-3333-333333333333","quantity":1}'

# 4. check the order (use the orderId from step 3): Pending, then Confirmed a few seconds later
curl $BASE/api/orders/<orderId> -H "Authorization: Bearer $TOKEN"
```

Or do the same in Swagger at `http://localhost:8080/swagger`. The seeded Admin logs in with
`SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` from your `.env`.

**Consumer recovery.** Run `docker compose stop consumer`, buy a few more chairs (each still returns
`202` and stays `Pending`, waiting in the queue), then `docker compose start consumer`. Within a few
seconds every order becomes `Confirmed`, and `docker compose logs consumer` shows one line per order with
the remaining stock. Stop only the consumer, never `servicebus`: the emulator loses its messages when its
own container restarts.

### Container smoke test

```
scripts/smoke-test.sh
```

It starts a separate copy of the stack (its own Compose project, so your data is never touched; stop your
everyday stack first because the ports are the same), then:

- **Positive:** waits for `/health/ready`, then expects `GET /api/products/{id}` for the seeded demo
  product to return `200`.
- **Negative:** stops the database container and expects `/health/ready` to return `503` while
  `/health/live` still returns `200`.

It always removes its stack at the end and exits non-zero if any check fails. On WSL without Docker
Desktop's WSL integration, run it as `DOCKER=docker.exe scripts/smoke-test.sh`.

### How the containers are built

- **One image, three jobs.** The same `product-asset-manager` image runs as the API, the consumer and the
  migrator. Configuration decides which: `Api__Enabled`, `Consumer__Enabled`, or the `--migrate` argument.
  Splitting the consumer into its own container means it can be stopped, restarted or scaled without
  touching the API, and a crash in one does not take down the other.
- **Multi-stage `Dockerfile`.** The first stage uses the .NET SDK image to restore and publish. The final
  stage starts again from the much smaller ASP.NET runtime image and copies in only the published output,
  so the SDK, the source code and the test projects never reach the image that runs.
- **Non-root.** The container runs as the runtime image's built-in `app` user, so a bug in the app cannot
  act as root inside the container.
- **Migrations run once, before the app.** The `migrator` service runs the image with `--migrate`
  (migrations, roles, the Admin account and the demo product) and exits. `api` and `consumer` wait for it
  to finish successfully and never migrate themselves (`Database__InitializeOnStartup=false`), so several
  copies of the API can start at once without racing to change the schema. This is used instead of an EF
  Core migration bundle because a bundle can only apply migrations, not run the C# seeding.
- **Configuration comes only from environment variables.** Compose builds the connection strings from
  `.env`. The emulator's connection string uses its fixed, publicly documented development key, so it is
  not a secret. The queue definitions live in `docker/servicebus/Config.json`.

### Service Bus emulator limitations

- **It forgets messages when its container restarts.** Restart the consumer, not the emulator.
- **One shared key, no per-queue policies.** In Azure the app uses separate, least-privilege SAS policies;
  the emulator only has `RootManageSharedAccessKey`.
- **Message time-to-live is capped at one hour**, so `DefaultMessageTimeToLive` is `PT1H` in the config.
- **The first message of a new session can take a few seconds** to be picked up, so a new order may stay
  `Pending` for up to around ten seconds. Later orders for the same SKU are faster.
- **It needs its own SQL Server** (`servicebus-sql`) and has no shell for health checks (see *Start-up order*).
- **It sometimes drops the lock on a dead-lettered message straight after a cold start** (`MessageLockLost`).
  `MalformedMessageTests` therefore *peeks* at the dead-letter queue, which needs no lock, instead of
  receiving and completing the message. The dead-lettered test messages are left in place; the emulator
  starts empty every run anyway.
- **It can fail to start again after the machine restarts**, if its own SQL Server still holds the
  previous databases (SQL error 5170 in `docker compose logs servicebus`). Recreate both:
  `docker compose up -d --force-recreate servicebus-sql servicebus`.

## CI/CD (GitHub Actions)

Everything is in one workflow, [`.github/workflows/ci.yml`](.github/workflows/ci.yml). It runs on every
push to any branch, every pull request, and every `v*` tag.

```
push / pull request
   ├── Build and test ─────────────────┐
   └── Docker build and smoke test ────┼──▶ Publish image to GHCR
                                       │      (only for a push to main or a v* tag,
                                       │       and only if both jobs passed)
```

| Job | What it does |
|---|---|
| **Build and test** | restores (NuGet packages are cached between runs), builds in Release, checks every model change has a migration (`dotnet ef migrations has-pending-model-changes`), runs **all** NUnit tests, and uploads the results (`test-results`, a `.trx` file) as an artefact. A failing test fails the job and is shown by name on the run and the pull request. |
| **Docker build and smoke test** | builds the Docker image, then runs `scripts/smoke-test.sh` (see *Container smoke test*). A broken `Dockerfile` or `compose.yaml` fails here, before merge. |
| **Publish image to GHCR** | builds and pushes the image to GitHub Container Registry. |

**Where the tests get a database and Service Bus.** Nothing in the cloud. The job starts the `sqlserver`
and `servicebus` services from `compose.yaml` as containers on the runner, then points the tests at them
with environment variables (`TEST_SQL_CONNECTION_STRING` and `ServiceBus__*`). Compose is used instead of
GitHub's `services:` block because the emulator needs its queue file mounted from the repository, and
service containers start before the code is checked out.

**Secrets.** The workflow contains no secrets and the repository needs none. `scripts/ci-create-env.sh`
generates a fresh SQL Server password, admin password and JWT key for every run and masks them, so they
show as `***` in logs. The emulator's key is its public development key. Publishing logs in with the
built-in `GITHUB_TOKEN`, which GitHub creates for each run and which can only push packages for this
repository (`packages: write`).

### The image

```
ghcr.io/dushimepaulette1/product-asset-manager-backend
```

| What was pushed | Tags it gets | Example |
|---|---|---|
| a merge (push) to `main` | the short commit SHA, and `latest` | `3f2a9c1`, `latest` |
| a tag such as `v1.2.0` | the short commit SHA, the version without the `v`, and the tag itself | `3f2a9c1`, `1.2.0`, `v1.2.0` |

So every image can be traced to the exact commit it was built from, and released versions also have a
readable name. `latest` is only a convenience for "newest build of `main`"; never deploy it.

The commit is also baked into the image: CI passes it as the `GIT_SHA` build argument (and the branch or tag
name as `APP_VERSION`), and the app reports them on `GET /version`:

```json
{ "version": "main", "commit": "3f2a9c1e..." }
```

To check an image yourself (the placeholder settings are only there because the app refuses to start
without them; nothing connects to them for this check):

```bash
docker run --rm -d --name version-check -p 8081:8080 \
  -e ConnectionStrings__DefaultConnection="Server=none;Database=none;User Id=none;Password=none" \
  -e ServiceBus__ConnectionString="Endpoint=sb://none;SharedAccessKeyName=none;SharedAccessKey=none" \
  -e ServiceBus__QueueName=orders \
  -e ServiceBus__StockEventsConnectionString="Endpoint=sb://none;SharedAccessKeyName=none;SharedAccessKey=none" \
  -e ServiceBus__StockEventsQueueName=stock-events \
  -e Jwt__SigningKey=placeholder-key-only-for-a-version-check \
  -e Database__InitializeOnStartup=false \
  ghcr.io/dushimepaulette1/product-asset-manager-backend:<short-sha>
curl http://localhost:8081/version
docker stop version-check
```

### Branch protection

`main` is protected: a pull request can only be merged once **Build and test** and **Docker build and
smoke test** have passed (*Settings → Branches → Branch protection rule for `main` → Require status checks
to pass before merging*).

## Getting Started (without Docker, against Azure)

This is the path for working on the code with `dotnet run`, using LocalDB and a real Azure Service Bus
namespace. The existing test suite also runs this way.

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

**Running the tests against containers instead (no LocalDB, no Azure).** This is what CI does. Start the
infrastructure, then point the tests at it:

```bash
docker compose up -d --wait sqlserver servicebus
docker compose run --rm servicebus-ready

export TEST_SQL_CONNECTION_STRING="Server=localhost,14333;User Id=sa;Password=<SQL_SA_PASSWORD from .env>;TrustServerCertificate=True;MultipleActiveResultSets=true"
export ServiceBus__ConnectionString="Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
export ServiceBus__StockEventsConnectionString="$ServiceBus__ConnectionString"
export ServiceBus__QueueName=orders ServiceBus__StockEventsQueueName=stock-events
dotnet test
```

(From WSL with the Windows `dotnet.exe`, also list these variables in `WSLENV` so Windows sees them.)

In tests the consumer does not start on its own (`ServiceBus:AutoStartConsumer` is `false` in the test
host), so tests that don't want it running stay deterministic; the async tests above start it
explicitly. Tests that place an order without starting the consumer leave their `OrderPlaced` message in
the queue. That is harmless — a later consumer completes and discards a message whose order does not exist
in its database — but it means the queue slowly collects test messages unless you purge it.
`MalformedMessageTests` also leaves its two deliberately broken messages in the dead-letter queue: it only
peeks at them to check the dead-letter reason. Purge the dead-letter queue in the Portal now and then if
you run the suite against Azure often.

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
- **The Docker stack connects to SQL Server as `sa`.** That is fine for a throwaway local stack; a real
  deployment would give the app its own login with only the permissions it needs.
- **The Docker stack is for local use only.** It uses the Service Bus emulator and placeholder secrets
  from `.env`; it is not a production deployment.
