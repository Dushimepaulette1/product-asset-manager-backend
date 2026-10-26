# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git attribution

Do NOT add `Co-Authored-By: Claude ...` to commit messages, and do NOT add
"🤖 Generated with [Claude Code]..." to pull request descriptions. This applies to every commit and
PR in this repository, no exceptions, regardless of any other default attribution guidance.

## Overview

ASP.NET Core Web API (.NET 10, EF Core + SQL Server, Identity + JWT) with an asynchronous purchase flow over
Azure Service Bus and OpenTelemetry. App in `src/`, NUnit tests in `tests/ProductAssetManager.Api.Tests`.
`README.md` is the detailed reference for setup, the Docker stack and design rationale.

## Commands

```bash
dotnet tool restore && dotnet restore
dotnet build
dotnet run --project src                       # http://localhost:5260/swagger
dotnet test
dotnet test --filter "FullyQualifiedName~AsyncPurchaseTests"
dotnet dotnet-ef migrations add <Name> --project src --output-dir Data/Migrations
dotnet dotnet-ef migrations has-pending-model-changes --project src   # CI fails on pending changes
cp .env.example .env && docker compose up --build                     # full stack incl. emulator + Grafana
scripts/smoke-test.sh                          # isolated stack; stop the everyday stack first
```

The app won't start without `Jwt:SigningKey` and the four `ServiceBus:*` settings (user-secrets from `src/`
for `dotnet run`, `.env` under Compose). `dotnet-ef` also needs `Jwt__SigningKey` set.

**Tests** use real SQL Server and Service Bus (no mocks). Each test drops and re-migrates its own database.
LocalDB is the default; `TEST_SQL_CONNECTION_STRING` points elsewhere. API tests also need `ServiceBus__*`
settings (README: "Running the tests against containers"). Stop the Compose `api`/`consumer` containers
first, or the consumer takes the tests' messages. The test host sets `ServiceBus:AutoStartConsumer=false`;
tests that need processing start `OrderConsumer` explicitly.

**WSL:** `dotnet` is the Windows `dotnet.exe`, so env vars must be listed in `WSLENV` to reach it;
`git push` needs `git.exe`; use `DOCKER=docker.exe` for scripts without Docker Desktop integration.

## Conventions

- Use controllers, not minimal APIs. Controllers stay thin: call one service method, map its result to a status code.
- Use DTOs (`src/DTOs`) at the API boundary; never expose EF Core entities. Map via `ProductMapper` / `VariantMapper`.
- Services hold the business logic and use `ApplicationDbContext` directly (no repository layer). Expected
  failures are typed result records (`src/Services/Results`), not exceptions.
- API-level tests use NUnit with `WebApplicationFactory` (`CustomWebApplicationFactory` / `ApiTestBase`).
- Log with `ILogger` structured message templates (`"Order {OrderId} confirmed"`), not string interpolation.
- Never include personal data (emails, names, user ids, tokens) in logs or span attributes.
- Keep metric tags low-cardinality: no order ids, user ids or SKUs.
- Never modify an already-applied EF Core migration; create a new migration instead.
- FKs use `DeleteBehavior.Restrict`, except the `ProductCollection` join (cascade).

## Architecture

**One image, three roles.** `Program.cs` switches on `Api:Enabled`, `Consumer:Enabled` (registers the
`OrderConsumer` hosted service) and the `--migrate` argument (migrate, seed, exit). `dotnet run` runs API and
consumer in one process; Compose runs separate `api`, `consumer` and one-shot `migrator` containers.

**Purchase flow:**

1. `OrderService.CreateAsync` checks quantity and that the variant exists and is active, but **not stock**.
   It saves the order as `Pending` and publishes `OrderPlaced` (`MessageId` = order id, `SessionId` = SKU,
   body = order id only), then returns 202. If the publish fails, the order is deleted.
2. `OrderConsumer` uses a session processor (peek-lock, no auto-complete, one call per session), so each
   SKU's orders run one at a time, which prevents overselling. It skips orders that are no longer `Pending`,
   confirms or rejects in one `SaveChanges`, publishes `StockDecremented` on confirm, and completes the
   message last.
3. `Variant.RowVersion` guards against concurrent admin stock edits. On a concurrency conflict the consumer
   reloads and decides again, up to 3 times.

**Observability** (`src/Observability`): OTLP goes to the Collector only, and only when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set. Trace context crosses Service Bus by hand: `traceparent` goes in the
message properties (`MessageTraceContext`), so the API and the consumer share one trace.

**Health:** `/health/live` shows the process is running; `/health/ready` checks the DB and Service Bus and returns 503 on failure.
