# Prepaid Engine

Prepaid Engine is a backend service responsible for managing the end-to-end prepaid billing lifecycle for smart-meter consumers. It generates prepaid bills based on consumption and tariff data, integrates with RMS for billing and recharge processing, and manages consumer disconnection and reconnection workflows.

## Structure

- `backend/` — .NET 8 solution (`PrepaidEngine.sln`)
  - `PrepaidEngine.Api` — ASP.NET Core Web API (entry point)
  - `PrepaidEngine.Application` — use cases / business logic orchestration
  - `PrepaidEngine.Domain` — core domain models, no external dependencies
  - `PrepaidEngine.Infrastructure` — data access, external integrations (e.g. RMS)
  - `PrepaidEngine.Tests` — xUnit test project
- `frontend/` — Angular + TypeScript

## Getting Started

### Backend
```bash
cd backend
dotnet build PrepaidEngine.sln
dotnet run --project PrepaidEngine.Api
```

Data access uses EF Core with **PostgreSQL** (Npgsql) as the configured provider. Note: **RMS
remains the authoritative system of record for the consumer's real financial wallet** — the
`PrepaidWallets`/`WalletTransactions` tables here are the Prepaid Engine's own working ledger
used for billing/recharge orchestration, not a competing wallet.

#### Database setup

1. Create the database once: `createdb prepaid_engine` (or via `psql`: `CREATE DATABASE prepaid_engine;`).
2. Set the real connection string via .NET User Secrets (never commit real credentials —
   `appsettings.json` only holds a placeholder password):
   ```bash
   cd backend/PrepaidEngine.Api
   dotnet user-secrets set "ConnectionStrings:PrepaidEngine" "Host=localhost;Port=5432;Database=prepaid_engine;Username=postgres;Password=<your-password>"
   ```
3. Apply migrations:
   ```bash
   dotnet tool restore
   dotnet tool run dotnet-ef database update \
     --project backend/PrepaidEngine.Infrastructure/PrepaidEngine.Infrastructure.csproj \
     --startup-project backend/PrepaidEngine.Api/PrepaidEngine.Api.csproj
   ```
4. Verify: `dotnet run --project backend/PrepaidEngine.Api`, then `curl http://localhost:5299/health` → `{"status":"Healthy"}`, and Swagger UI at `http://localhost:5299/swagger`.

In Development, the app auto-applies any pending migrations and seeds one demo consumer
(`DEMO-0001`) end-to-end through tariff/consumption/billing/recharge on startup (see
`DbSeeder`) — safe to leave on since it's a no-op once a consumer already exists, and never
runs outside Development.

#### Demo endpoints (local/no auth yet — see docs/assumptions-and-security.md)

- `GET /api/v1/consumers` — list consumers with wallet balance
- `GET /api/v1/consumers/{accountNumber}` — full detail: meter, wallet ledger, bills (try `DEMO-0001`)

Add a new migration after changing the model:
```bash
dotnet tool run dotnet-ef migrations add <Name> \
  --project backend/PrepaidEngine.Infrastructure/PrepaidEngine.Infrastructure.csproj \
  --startup-project backend/PrepaidEngine.Api/PrepaidEngine.Api.csproj \
  --output-dir Persistence/Migrations
```

### Backend tests
```bash
cd backend
dotnet test PrepaidEngine.sln
```

### Frontend
```bash
cd frontend
npm install
npm start
```
