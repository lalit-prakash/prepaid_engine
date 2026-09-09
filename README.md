# Prepaid Engine

Prepaid Engine is a backend service responsible for managing the end-to-end prepaid billing
lifecycle for smart-meter consumers. It generates prepaid bills based on consumption and
tariff data, integrates with RMS for billing and recharge processing, and manages consumer
disconnection and reconnection workflows.

**Scope**: this repository is the Prepaid Engine only — not an MDMS, HES, AMI, SCADA, or meter
communication platform. RMS is the authoritative system of record for the consumer's real
financial wallet; the Prepaid Engine's own `PrepaidWallets`/`WalletTransactions` tables are a
working ledger for billing/recharge orchestration, not a competing wallet.

**Current status**: backend domain + persistence + a mock RMS integration + a small demo API,
all verified against real data (a live PostgreSQL database and MePDCL's own tariff book +
reference calculation workbooks). Frontend is intentionally paused — see
[Frontend](#frontend) below.

## Structure

- `backend/` — .NET 8 solution (`PrepaidEngine.sln`)
  - `PrepaidEngine.Api` — ASP.NET Core Web API (entry point, demo endpoints, Basic auth, seeding)
  - `PrepaidEngine.Application` — use cases / integration ports (currently: `IRmsClient`)
  - `PrepaidEngine.Domain` — core domain models and business rules, no external dependencies
  - `PrepaidEngine.Infrastructure` — EF Core persistence (PostgreSQL), mock RMS adapter, seed data
  - `PrepaidEngine.Tests` — xUnit test project (104 tests — see [Testing](#testing))
- `frontend/` — Angular + TypeScript (default CLI scaffold only; paused)
- `docs/` — sourcing, security, and tariff-validation documentation (see [Documentation](#documentation))

## Domain model

| Entity | Purpose |
|---|---|
| `Consumer` | A prepaid consumer: account, meter, connected load, connection status, owns a `PrepaidWallet` |
| `SmartMeter` | Meter number, phase (single/three), cumulative reading |
| `Tariff` / `TariffSlab` | Versioned, category-scoped slab tariff: energy slabs, fixed charge, prepaid rebate %, emergency-credit limit, per-phase vend limits |
| `ElectricityDuty` | Statutory per-unit duty, category-based (Domestic/BPL flat, Industrial tiered, Others flat) — modeled separately from `Tariff` since it isn't tariff-plan-specific |
| `TransformerMaintenanceCharge` / `CtPtMaintenanceCharge` | Opt-in fixed monthly maintenance charges (TMC, CPMC) for consumer-owned transformers/CT-PT sets, by voltage and (for CPMC) wiring |
| `PrepaidWallet` / `WalletTransaction` | Balance + append-only ledger; tracks emergency-credit usage separately from the normal balance |
| `ConsumptionReading` | A metered consumption reading for a billing period |
| `PrepaidBill` | A generated bill with payment/status tracking (`Generated`/`Paid`/`PartiallyPaid`/`Overdue`/`Cancelled`) |
| `RechargeTransaction` | A recharge processed through RMS, with its own status lifecycle (`Initiated`/`Success`/`Failed`/`Reversed`) |
| `FppasCharge` | A notified FPPAS (Fuel and Power Purchase Adjustment Surcharge) rate change, deferred one billing month and prorated across every day of the following month |

### Tariff engine — verified calculation methods

All figures below are sourced from the MePDCL Electricity Distribution Tariff (effective
1 Apr 2026) and cross-checked against MePDCL's own reference calculation workbooks. Full
sourcing detail, including one documented tariff-vs-Excel discrepancy, is in
[`docs/tariff-validation-report.md`](docs/tariff-validation-report.md).

- **`Tariff.CalculateEnergyCharge(consumptionKwh)`** — slab-cumulative energy charge from zero.
- **`Tariff.CalculateEnergyChargeForPeriod(previousCumulative, currentCumulative)`** — the
  correct way to bill a single day/period from a cumulative smart-meter reading against
  monthly slabs (`= CalculateEnergyCharge(current) - CalculateEnergyCharge(previous)`).
  Verified against real day-by-day rows spanning slab boundaries.
- **`Tariff.CalculateDailyFixedCharge(load)`** — daily-prorated fixed charge:
  `MonthlyRate × Load × 12 / 365` (fixed 365 divisor, confirmed from the reference workbook;
  accrues even on zero-consumption days).
- **`Tariff.CalculateNetPrepaidCharge(...)`** — monthly-cycle convenience combining energy
  charge, the 2% prepaid rebate, and fixed charge (used by the demo seeder).
- **`Tariff.ValidateVendAmount(amount, phase)`** — enforces per-phase min/max recharge limits.
- **`ElectricityDuty.Calculate(category, consumptionKwh)`** / **`CalculateForPeriod(...)`** —
  category-aware duty, mirroring the energy-charge cumulative-differencing pattern for
  Industrial's tiered slabs.
- **BPL/Kutir Jyoti** consumers are modeled as an ordinary 4-slab `Tariff` (0–30@₹4.57,
  30–100@₹5.00, 100–200@₹5.04, 200+@₹5.10) — no special-case code needed; the tariff book's
  "first 30 kWh special rate, excess at normal domestic slabs" rule turns out to be exactly a
  standard cumulative slab table once the DLT boundaries stay absolute.

### FPPAS engine

`FppasCharge` implements the notification-lag/proration mechanism sourced entirely from
MePDCL's reference workbook (the tariff book only states FPPAS is monthly, not the mechanism):

- **`TotalAmount`** = the prior month's gross energy charge × the notified rate (a signed
  fraction, e.g. `-0.14` for a 14% decrease). Verified against a negative and a positive
  worked example (₹6,000 × -14% = -₹840; ₹3,250 × +6.65% = ₹216.125).
- **`DetermineApplicableBillingMonth(sourceBillDate)`** — a notified rate is never applied to
  the bill it was computed from; it's deferred to the *next* billing month.
- **`AllocateAcrossDays(daysInMonth)`** — spreads the total evenly, unrounded, across every day
  of that month (matches the workbook's shown precision exactly, including a non-terminating
  repeating decimal case: ₹216.125 ÷ 31 days = ₹6.971774193548387…/day).
- **`AllocateAcrossDaysRoundedToCents(daysInMonth)`** — the paisa-accurate variant for actually
  posting to a ledger, where the last day absorbs the rounding residual so the sum always
  reconciles exactly to the total (needed since the workbook's own unrounded figures don't sum
  to a clean rupee-and-paisa amount).

**Wired into `PrepaidBill`** — a bill carries `FppasAmount` (this bill's daily share) and
`FppasChargeId` (a traceable link back to the notification it came from), both exposed via
`GET /api/v1/consumers/{accountNumber}`. Verified live against real PostgreSQL: a ₹225 gross
energy charge with a 2% FPPAS notification correctly produces `225.00 − 4.50 (rebate) + 180.00
(fixed) + 2.25 (duty) + 0.15 (FPPAS share) = ₹402.90`. Still missing: automatic scheduling of
*when* a newly notified rate gets picked up for the next cycle — today the caller constructs
the `FppasCharge` and passes its daily share in explicitly (see `DbSeeder`).

### TMC and CPMC (transformer / CT-PT maintenance charges)

Standalone calculators, same pattern as `ElectricityDuty` — sourced from the tariff book (no
example values exist in either reference workbook, so nothing to cross-check numerically):

- **`TransformerMaintenanceCharge.CalculateForExclusiveUse(voltage, optedIn, installedCapacityKva)`**
  / **`.CalculateForSharedUse(voltage, optedIn, contractedDemandOrLoadKva)`** — ₹20/kVA/month at
  11 kV or 33 kV, ₹25/kVA/month at 132 kV; zero unless the consumer opted in. Basis differs by
  whether MePDCL also uses the transformer's spare capacity for other consumers (§5.1–5.2) —
  choosing the right one is the caller's job, since that's a fact about the consumer's
  equipment this calculator has no way to know.
- **`CtPtMaintenanceCharge.Calculate(voltage, wiring, optedIn)`** — a flat monthly rate by
  voltage and CT-PT wiring (₹800/1,000/1,500/1,900), zero unless opted in; throws for 132 kV
  (no rate defined in the tariff book) unless not opted in, in which case it's zero regardless.

**Not yet wired into `PrepaidBill`** (unlike FPPAS) — both depend on per-consumer equipment
facts (transformer/CT-PT ownership, maintenance opt-in, exclusive vs. shared use) that aren't
modeled on `Consumer` yet, and neither workbook has a worked example to verify a wired-in bill
against.

**Explicitly not yet implemented** (see `docs/tariff-validation-report.md` for detail on each):
arrear recovery, ToD/peak tariffs for Industrial HT/EHT, non-communicating meter
estimated billing, delayed payment charges, disconnection/reconnection fee schedule.

## Recharge flow

`POST /api/v1/consumers/{accountNumber}/recharge` orchestrates recharge through `IRmsClient`
(currently `MockRmsClient`, an in-memory simulator — see the `TODO` in `Program.cs` for where a
real HTTP-based RMS adapter plugs in):

1. Caller supplies a **required, stable** `IdempotencyKey` (the server does not generate one —
   a server-generated key wouldn't survive a client retry after a lost response, which would
   defeat the whole guarantee).
2. Calls RMS. On `RmsUnavailableException`, returns `503` without touching the wallet.
3. Checks our own ledger for the RMS reference (handles the case where RMS already processed
   this idempotency key from a prior, failed-to-persist attempt).
4. On RMS `Success`: credits the wallet, returns `200`.
   On `Failed`: records the attempt, returns `402`, no credit.
   On `Pending`: records the attempt, returns `202`, no credit.
5. A repeated `RmsReferenceId` or `IdempotencyKey` always replays the original result —
   verified live against real concurrent calls (20 parallel calls with one key produce exactly
   one RMS reference).

In `MockRmsClient`, prefix your `IdempotencyKey` with `FAIL-`, `PENDING-`, or `UNAVAILABLE-` to
force those outcomes for testing.

## Getting Started

### Backend
```bash
cd backend
dotnet build PrepaidEngine.sln
dotnet run --project PrepaidEngine.Api
```

Data access uses EF Core with **PostgreSQL** (Npgsql) as the configured provider.

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

Add a new migration after changing the model:
```bash
dotnet tool run dotnet-ef migrations add <Name> \
  --project backend/PrepaidEngine.Infrastructure/PrepaidEngine.Infrastructure.csproj \
  --startup-project backend/PrepaidEngine.Api/PrepaidEngine.Api.csproj \
  --output-dir Persistence/Migrations
```

#### API endpoints

| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /health` | none (health-probe convention) | Liveness check |
| `GET /api/v1/consumers` | HTTP Basic | List consumers with wallet balance |
| `GET /api/v1/consumers/{accountNumber}` | HTTP Basic | Full detail: meter, wallet ledger, bills |
| `POST /api/v1/consumers/{accountNumber}/recharge` | HTTP Basic | Recharge flow (see above) |
| `GET /swagger` | none (Development only) | Interactive API docs |

Set Basic-auth demo credentials before running (`appsettings.json` only holds `CHANGE_ME`
placeholders):
```bash
cd backend/PrepaidEngine.Api
dotnet user-secrets set "DemoAuth:Username" "<username>"
dotnet user-secrets set "DemoAuth:Password" "<password>"
```
Then: `curl -u <username>:<password> http://localhost:5299/api/v1/consumers/DEMO-0001`.

**These endpoints are demo-only** — HTTP Basic with one shared password, no per-user identity,
no token expiry, no rate limiting. See
[`docs/assumptions-and-security.md`](docs/assumptions-and-security.md) for the full security
posture and what's required before any shared/production exposure.

### Testing

```bash
cd backend
dotnet test PrepaidEngine.sln
```

**104 tests, all passing.** Breakdown:

| Test class | Count | What it covers |
|---|---|---|
| `TariffTests` | 12 | Slab energy charge, fixed charge, rebate composition, vend-amount validation |
| `TariffGoldenDataTests` | 9 | **Regression against real day-by-day rows from MePDCL's own reference workbook** — energy charge crossing slab boundaries, daily fixed-charge proration, full net-bill composition, including a zero-consumption day |
| `BplTariffTests` | 6 | BPL/Kutir Jyoti 4-slab tariff (special first-30-kWh rate + normal domestic slabs) |
| `DhtTariffDiscrepancyTests` | 2 | The documented DHT ₹5.85 (production) vs. ₹5.87 (legacy Excel reference) discrepancy — both kept as explicit, separately named tests, neither silently overriding the other |
| `ElectricityDutyTests` | 15 | Category-based duty: Domestic/BPL flat rate, "Others" flat rate, Industrial tiered slabs (including cumulative-position vs. raw-delta correctness), negative-input validation |
| `FppasChargeTests` | 10 | **Regression against both worked FPPAS examples in the reference workbook** — negative and positive rate cases, unrounded daily proration matching the workbook's exact precision, the paisa-accurate rounded variant, applicable-billing-month scheduling, input validation |
| `PrepaidBillTests` | 6 | Charge-breakdown composition (energy net + fixed + duty + FPPAS), positive/negative FPPAS shares, rebate/negative-FPPAS validation guards, payment application with FPPAS included |
| `TransformerMaintenanceChargeTests` | 4 | TMC by voltage (11/33/132 kV), opt-in/opt-out, exclusive- vs. shared-use billing basis, negative-input validation |
| `CtPtMaintenanceChargeTests` | 7 | CPMC by voltage/wiring combination, opt-in/opt-out, undefined-132kV-rate handling |
| `PrepaidWalletTests` | 12 | Wallet credit/debit, emergency-credit tracking, consumer connect/disconnect/reconnect rules |
| `MockRmsClientTests` | 9 | Recharge success/failed/pending/unavailable outcomes, idempotent replay, **20-way concurrent-call race test**, input validation, transaction-status lookup |
| `PrepaidEngineDbContextTests` | 5 | Real persistence round-trips against SQLite (keys, FKs, owned collections) — including a regression test for a real EF change-tracking bug found while building the recharge endpoint (crediting an already-loaded wallet), and a `PrepaidBill`↔`FppasCharge` round-trip |

Every number in `TariffGoldenDataTests`, `BplTariffTests`, and `DhtTariffDiscrepancyTests` is
taken verbatim from MePDCL's tariff book or reference workbooks, not invented — a failure there
means a real divergence from the utility's own numbers, not a made-up expectation.

### Frontend

**Paused** — this is a deliberate, explicit decision (not a gap), pending further instruction.
The `frontend/` directory currently holds only the default `ng new` scaffold.

```bash
cd frontend
npm install
npm start
```

## Documentation

- [`docs/assumptions-and-security.md`](docs/assumptions-and-security.md) — what's verified
  against a real source vs. assumed; a running security checklist against what's actually
  implemented (not a blanket compliance claim).
- [`docs/tariff-validation-report.md`](docs/tariff-validation-report.md) — every tariff rule
  implemented, its source (tariff book / Excel / both), the production decision where sources
  disagree, and which test case verifies it.
