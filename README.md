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
| [API_REFERENCE.md](docs/API_REFERENCE.md) | Every endpoint: method, path, who may call it, parameters (generated from the code) |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | System design, API surface, flows, workers, scale approach, frontend, known gaps |
| [DOMAIN_RULES.md](docs/DOMAIN_RULES.md) | Tariff, FPPAS, TMC/CPMC, arrears, ToD, DLP billing, recharge, RC/DC, conversion rules |
| [assumptions-and-security.md](docs/assumptions-and-security.md) | Regulatory sourcing, assumptions, security checklist |
| [tariff-validation-report.md](docs/tariff-validation-report.md) | How the tariff engine was checked against the reference workbooks |

Work tracking: https://github.com/users/lalit-prakash/projects/5

## What is built

Dashboard, Consumers (server-searched list, tabbed detail, postpaid→prepaid conversion KPIs), Recharge
operations, Meter credit, RC/DC (disconnection held to the tariff book's 11 AM-4 PM window), Conversion,
Reconciliation, Exceptions, Billing (with tariff-version bill detail), Tariffs and tariff governance (IT
drafts, Utility approves, automatic activation, a book-check against the FY 2026-27 tariff book) and the
calculation workbench, Meter data (DLP/BP/LS/IP/events/alarms, kVAh and Time-of-Day billing from the load
survey), Reports (with zone-to-DTR network filters and breakdowns), Network hierarchy import, Audit logs,
SLA monitoring, Billing holds, Notifications, Meter replacements, User Management (with Roles &amp;
Permissions) and System Settings. RMS, meter-command and connectivity integrations are **mock adapters**
for local and UAT use (the Integrations page says so per adapter); Service Requests is still a placeholder.
See [ARCHITECTURE.md](docs/ARCHITECTURE.md) §12 for the full gap list.

## Requirements

.NET 8 SDK, Node.js 18+ with npm, and a running PostgreSQL server.

## Run locally

### 1. Database and secrets
```bash
createdb prepaid_engine          # or: CREATE DATABASE prepaid_engine;

cd backend/PrepaidEngine.Api
dotnet user-secrets set "ConnectionStrings:PrepaidEngine" "Host=localhost;Port=5432;Database=prepaid_engine;Username=postgres;Password=<your-password>"

# Sign-in users (choose your own values; never commit them). Roles: Admin, IT, Operator, Utility, ReadOnly (see docs/assumptions-and-security.md).
# Generate the hash first:  dotnet run --project backend/PrepaidEngine.Api -- hash-password "<password>"
dotnet user-secrets set "DemoAuth:Users:0:Username"     "<login-id>"
dotnet user-secrets set "DemoAuth:Users:0:DisplayName"  "<name shown in the header>"
dotnet user-secrets set "DemoAuth:Users:0:PasswordHash" "<hash from the command above>"
dotnet user-secrets set "DemoAuth:Users:0:Role"         "IT"
# JWT signing key, 32+ characters (Development falls back to a random per-run key if this is unset).
dotnet user-secrets set "Jwt:Key" "<long-random-string>"
```
`appsettings.json` only contains `CHANGE_ME` placeholders. In Development the API applies pending
migrations and seeds demo data (consumers, tariffs from the FY 2026-27 tariff book, bills, recharges,
network hierarchy) on startup; it does neither outside Development. Sign-in users are stored in the
database (`AppUser`) and managed from the User Management screen; `DemoAuth:Users:*` secrets below are
only a bootstrap login for the very first run.

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
Open `http://localhost:4200` and sign in with the login id and password of a user you created. The IT user drafts and
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
cd backend && dotnet test          # 661 tests: domain rules, services, adapters, EF mapping, tariff activation and billing
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
Authentication is JWT bearer: `POST /api/v1/auth/login` returns a 30-minute signed token (renewable up to
8 hours), passwords are stored as PBKDF2 hashes, and repeated failed sign-ins lock the login id for 15
minutes. "Forgot password?" on the login page e-mails a 6-digit code (10 minutes, single use) to the user's
registered address; that needs SMTP settings (`Email:*`) and an `Email` per user. There are five roles with
per-endpoint policies, managed from User Management, and every write endpoint must name one. The API also
applies per-IP rate limiting, security headers, HSTS and a request size cap. Load testing at scale has not
been done. Do not expose this API beyond a trusted network until the remaining items are done (see
[assumptions-and-security.md](docs/assumptions-and-security.md)).
