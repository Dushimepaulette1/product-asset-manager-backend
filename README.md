# Product Asset Manager — Backend

ASP.NET Core Web API backend for an e-commerce inventory and order management platform (Backend Capstone).

## Local Development

### Prerequisites

- .NET SDK (net10.0)
- SQL Server LocalDB

### Database

```
cd src
dotnet ef database update
```

### JWT signing key (local secret)

The JWT signing key is never committed to source control. Set your own locally before running the app:

```
cd src
dotnet user-secrets set "Jwt:SigningKey" "<a long random string, 32+ characters>"
```

Any sufficiently long random value works — this app only ever validates tokens it signed itself, so there is no shared secret to obtain from anyone.

### Run

```
cd src
dotnet run
```

Swagger UI is available at `/swagger` when running in the Development environment.

### Seeded Admin account

On first startup, the app seeds the `Admin` and `User` roles and one Admin account, using the
credentials configured in `src/appsettings.Development.json` under `SeedAdmin`.

| Email | Password |
|---|---|
| `admin@local.dev` | `DevAdmin123!` |

This is a local development fixture only — it seeds a LocalDB database that only exists on
whichever machine runs the migration and the app. It is not a real secret and must never be
reused for any deployed/production instance.
