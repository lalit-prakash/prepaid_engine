# Prepaid Engine

Prepaid billing, wallet and recharge, tariff governance, meter-command orchestration and an
operations UI for smart-meter consumers.

- **Backend:** .NET 8 minimal API, EF Core + PostgreSQL, layered Domain / Application / Infrastructure / Api.
- **Frontend:** Angular 22 (standalone components, no UI library).
- **Boundaries:** RMS is the source of truth for payments. The engine owns billing, the wallet ledger,
  tariff governance, command orchestration and audit. MDMS/HES own meter data and command execution.
  A received payment is **not** a credited meter — the two are tracked and shown separately.

Documentation lives in [`docs/`](docs):

| Document | Contents |
|---|---|
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | System design, API surface, flows, workers, scale approach, frontend, known gaps |
| [DOMAIN_RULES.md](docs/DOMAIN_RULES.md) | Tariff, FPPAS, TMC/CPMC, arrears, ToD, DLP billing, recharge, RC/DC, conversion rules |
| [assumptions-and-security.md](docs/assumptions-and-security.md) | Regulatory sourcing, assumptions, security checklist |
| [tariff-validation-report.md](docs/tariff-validation-report.md) | How the tariff engine was checked against the reference workbooks |

Work tracking: https://github.com/users/lalit-prakash/projects/5

## What is built

Dashboard, Consumers (server-searched list, tabbed detail), Recharge operations, Meter credit, RC/DC,
Conversion, Reconciliation, Exceptions, Billing (with tariff-version bill detail), Tariffs and tariff
governance (IT drafts, Utility approves, automatic activation), Meter data (DLP/BP/LS/IP/events/alarms),
Reports, Audit logs, Analytics, SLA monitoring, Billing holds, Notifications, Meter replacements and the
calculation workbench. RMS, meter-command and connectivity integrations are **mock adapters** for local
and UAT use; System Health, Service Requests, User Management, Roles & Permissions, Integrations and
System Settings are placeholders. See [ARCHITECTURE.md](docs/ARCHITECTURE.md) §12 for the full gap list.

## Requirements

.NET 8 SDK, Node.js 18+ with npm, and a running PostgreSQL server.

## Run locally

### 1. Database and secrets
```bash
createdb prepaid_engine          # or: CREATE DATABASE prepaid_engine;

cd backend/PrepaidEngine.Api
dotnet user-secrets set "ConnectionStrings:PrepaidEngine" "Host=localhost;Port=5432;Database=prepaid_engine;Username=postgres;Password=<your-password>"

# Demo sign-in users (choose your own values; never commit them). Roles are IT and Utility.
dotnet user-secrets set "DemoAuth:Users:0:Username" "<it-user>"
dotnet user-secrets set "DemoAuth:Users:0:Password" "<it-password>"
dotnet user-secrets set "DemoAuth:Users:0:Role"     "IT"
dotnet user-secrets set "DemoAuth:Users:1:Username" "<utility-user>"
dotnet user-secrets set "DemoAuth:Users:1:Password" "<utility-password>"
dotnet user-secrets set "DemoAuth:Users:1:Role"     "Utility"
```
`appsettings.json` only contains `CHANGE_ME` placeholders. In Development the API applies pending
migrations and seeds demo data (seven consumers, tariff, bills, recharges) on startup; it does neither
outside Development.

### 2. Backend (port 5043)
```bash
cd backend
dotnet build PrepaidEngine.sln
dotnet run --project PrepaidEngine.Api --urls http://localhost:5043
```
Check `http://localhost:5043/health` → `{"status":"Healthy"}`; Swagger UI at `/swagger`.

### 3. Frontend (port 4200)
```bash
cd frontend
npm install
npm start
```
Open `http://localhost:4200` and sign in with one of the users you created. The IT user drafts and
submits tariff changes; the Utility user approves, rejects or schedules them.

### Stopping
Press `Ctrl+C` in each terminal. If a backend process is orphaned:
`Get-Process PrepaidEngine.Api | Stop-Process` (PowerShell).

## Database migrations
```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> \
  --project backend/PrepaidEngine.Infrastructure --startup-project backend/PrepaidEngine.Api \
  --output-dir Persistence/Migrations
dotnet tool run dotnet-ef database update \
  --project backend/PrepaidEngine.Infrastructure --startup-project backend/PrepaidEngine.Api
```
Always inspect a generated migration before applying it (EF does not infer sensible defaults for
string-converted enum columns).

## Tests
```bash
cd backend && dotnet test          # 397 tests: domain rules, services, adapters, EF mapping, tariff activation
cd frontend && npm test            # scaffold spec only
cd frontend && npx ng build        # production build check
```
There are no API integration tests yet; see the project board.

## Repository layout
```
backend/   PrepaidEngine.{Domain,Application,Infrastructure,Api,Tests} + PrepaidEngine.sln
frontend/  Angular app (src/app/{core,shared,layouts,features})
docs/      architecture, domain rules, sourcing and security notes
```

## Security note
Authentication is HTTP Basic with two demo roles — a stop-gap for local and demo use, not a production
scheme. Do not expose this API beyond a trusted network until real token-based authentication and
role management are in place (see [assumptions-and-security.md](docs/assumptions-and-security.md)).
