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

Data access uses EF Core with SQL Server as the configured provider
(`ConnectionStrings:PrepaidEngine` in `PrepaidEngine.Api/appsettings.json`, defaulting to
LocalDB). Note: **RMS remains the authoritative system of record for the consumer's real
financial wallet** — the `PrepaidWallets`/`WalletTransactions` tables here are the Prepaid
Engine's own working ledger used for billing/recharge orchestration, not a competing wallet.

Apply migrations to a local SQL Server / LocalDB instance:
```bash
dotnet tool restore
dotnet tool run dotnet-ef database update \
  --project backend/PrepaidEngine.Infrastructure/PrepaidEngine.Infrastructure.csproj \
  --startup-project backend/PrepaidEngine.Api/PrepaidEngine.Api.csproj
```

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
