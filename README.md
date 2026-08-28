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

### Seeded Admin account (local secret)

On first startup in the Development environment, the app migrates the database and seeds the
`Admin`/`User` roles plus one Admin account, using `SeedAdmin:Email` (in
`appsettings.Development.json`) and `SeedAdmin:Password`, which is never committed. Set your own
locally before running the app:

```
cd src
dotnet user-secrets set "SeedAdmin:Password" "<any password meeting the Card 5 password policy>"
```

The seeded email is `admin@local.dev`. Seeding only runs in Development, against whichever LocalDB
database exists on your own machine — this account never exists anywhere shared or deployed.
