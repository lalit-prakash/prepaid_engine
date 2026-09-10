# Prepaid Engine

Prepaid Engine is a backend service responsible for managing the end-to-end prepaid billing
lifecycle for smart-meter consumers. It generates prepaid bills based on consumption and
tariff data, integrates with RMS for billing and recharge processing, and manages consumer
disconnection and reconnection workflows.

**Scope**: this repository is the Prepaid Engine only — not an MDMS, HES, AMI, SCADA, or meter
communication platform. RMS is the authoritative system of record for the consumer's real
financial wallet; the Prepaid Engine's own `PrepaidWallets`/`WalletTransactions` tables are a
working ledger for billing/recharge orchestration, not a competing wallet.

**Current status**: backend domain + persistence + a mock RMS integration + a small demo API +
a minimal hand-built demo console + an Angular enterprise-operations frontend covering the
real API surface (Overview, Consumers, Consumer 360, recharge), all verified against real data
(a live PostgreSQL database and MePDCL's own tariff book + reference calculation workbooks).
The frontend's remaining modules (Billing, Meter Credit, RC/DC, Reports, ...) are routed but
render an explicit "not yet backed" stub rather than invented data — see
[docs/frontend-scope.md](docs/frontend-scope.md) for the real-vs-planned boundary.

## Structure

- `backend/` — .NET 8 solution (`PrepaidEngine.sln`)
  - `PrepaidEngine.Api` — ASP.NET Core Web API (entry point, demo endpoints, Basic auth, seeding)
  - `PrepaidEngine.Application` — use cases / integration ports (currently: `IRmsClient`)
  - `PrepaidEngine.Domain` — core domain models and business rules, no external dependencies
  - `PrepaidEngine.Infrastructure` — EF Core persistence (PostgreSQL), mock RMS adapter, seed data
  - `PrepaidEngine.Tests` — xUnit test project (155 tests — see [Testing](#testing))
- `frontend/` — Angular 22 enterprise operations UI (see [Frontend](#frontend) below and
  [docs/frontend-scope.md](docs/frontend-scope.md))
- `docs/` — sourcing, security, tariff-validation, and frontend-scope documentation (see [Documentation](#documentation))

## Domain model

| Entity | Purpose |
|---|---|
| `Consumer` | A prepaid consumer: account, meter, connected load, connection status, owns a `PrepaidWallet` |
| `SmartMeter` | Meter number, phase (single/three), cumulative reading |
| `Tariff` / `TariffSlab` | Versioned, category-scoped slab tariff: energy slabs, fixed charge, prepaid rebate %, emergency-credit limit, per-phase vend limits |
| `ElectricityDuty` | Statutory per-unit duty, category-based (Domestic/BPL flat, Industrial tiered, Others flat) — modeled separately from `Tariff` since it isn't tariff-plan-specific |
| `TransformerMaintenanceCharge` / `CtPtMaintenanceCharge` | Opt-in fixed monthly maintenance charges (TMC, CPMC) for consumer-owned transformers/CT-PT sets, by voltage and (for CPMC) wiring |
| `ArrearRecovery` | Applies a payment against outstanding arrears — uncapped/arrears-first (tariff book) by default, or an optional caller-supplied recovery cap (RFP-indicative only) |
| `TouPeriod` | A Time-of-Day rate band (Normal/Peak/Off-peak) on a `Tariff` — only IHT/IEHT tariffs populate these |
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

**Wired into `PrepaidBill`** — `TmcAmount`/`CpmcAmount` are both included in `Amount`. Verified
live against real PostgreSQL: the existing residential DLT demo bill correctly still shows both
as zero (that consumer owns no transformer/CT-PT set), and unit tests hand-verify a non-zero
case (100 kVA transformer + an 11kV 3-wire CT-PT set, both opted in → ₹2,000 TMC + ₹800 CPMC
composed correctly into the total) since neither workbook has a worked example to
regression-test against. **Still missing**: per-consumer equipment facts (transformer/CT-PT
ownership, maintenance opt-in, exclusive vs. shared use) aren't modeled on `Consumer` — today
the caller computes `TmcAmount`/`CpmcAmount` via the calculators and passes them in explicitly,
same as FPPAS's daily share.

### Arrear recovery

`ArrearRecovery.Calculate(paymentAmount, outstandingArrears, maxRecoveryPercentOfPayment?)` —
two distinct rules from two different-strength sources, kept deliberately separate:

- **Default (no cap supplied)**: uncapped, arrears-first — matches tariff book §13.4 exactly
  ("any payment shall first be adjusted towards the arrears... and then current bills").
- **Optional cap**: a caller-supplied `maxRecoveryPercentOfPayment` limits how much of a
  payment (e.g. a prepaid recharge) can go to arrears regardless of how much is owed. This
  models the supplied RFP's own "indicative rule" of a 50% cap on prepaid recharges — **the RFP
  is a lower-authority source than the tariff book in this project (see
  `docs/tariff-validation-report.md`), so no percentage is hard-coded anywhere**; a cap is only
  ever applied if a caller explicitly configures one.

**Wired into `PrepaidBill`** — `ArrearsAmount` (prior arrears carried onto this bill, included
in `Amount`) and `ArrearsRecovered` (tracked separately for audit). `PrepaidBill.ApplyPayment`
actually calls `ArrearRecovery.Calculate` internally: an incoming payment is split against this
bill's own outstanding arrears first, per §13.4, before the rest counts toward current charges
— `AmountPaid`/`OutstandingAmount`/`Status` behave exactly as before regardless of that internal
split. Verified live against real PostgreSQL (existing demo bill correctly still shows both
fields as zero, `Amount` unchanged) and hand-verified for multi-installment recovery in tests.
**Still missing**: nothing computes `ArrearsAmount` automatically from a consumer's actual
unpaid bill history — a caller sums prior bills' `OutstandingAmount` and passes it in, same
pattern as FPPAS/TMC/CPMC.

### ToD (Time-of-Day) tariffs for Industrial HT/EHT

`Tariff` now supports Time-of-Day rate bands (only IHT/IEHT define these in the tariff book):

- **`TouPeriod`** — one band (e.g. "Peak", 17:00-23:00, ₹6.66/kVAh); `EndTime < StartTime`
  means it wraps past midnight (the Off-peak band, 23:00-06:00). Rates are stored verbatim from
  the tariff book, not derived from a "±X%" formula at runtime — the book's own published
  Off-peak figures are already rounded (IHT: 5.55 × 0.85 = 4.7175, published as 4.72).
- **`Tariff.ClassifyTimeOfDay(timeOfDay)`** — which band a given clock time falls in.
- **`Tariff.CalculateTouEnergyCharge(consumptionByBandLabel)`** — sums each band's own rate ×
  its consumption; an unrecognized band label throws rather than silently under-billing.
- A pure-ToD tariff (like IHT/IEHT) can be constructed with zero ordinary slabs — the
  constructor now only requires *either* slabs *or* ToD periods, not always slabs.

Verified against both the tariff book's IHT (₹5.55/6.66/4.72) and IEHT (₹6.60/7.92/5.61)
schedules exactly, including every stated time boundary (05:59→Off-Peak, 06:00→Normal,
16:59→Normal, 17:00→Peak, 22:59→Peak, 23:00→Off-Peak) and midnight wraparound. Confirmed live
persisting/reloading a ToD tariff against real PostgreSQL.

**Not wired into `PrepaidBill`** — `CalculateTouEnergyCharge` expects consumption already
bucketed by band, which this project doesn't produce from raw meter readings itself (that's
interval-data/MDM territory, outside this project's stated Prepaid Engine scope); it's meant
to be called with band totals handed in from upstream.

**Explicitly not yet implemented** (see `docs/tariff-validation-report.md` for detail on each):
non-communicating meter estimated billing, delayed payment charges, disconnection/reconnection
fee schedule.

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

### Demo console

A minimal, hand-built single-page UI (`PrepaidEngine.Api/wwwroot/index.html`) is served
statically by the API itself at `http://localhost:<port>/` — plain HTML/CSS/JS, no build step,
no framework, and **not** the paused Angular frontend. It signs in with the same HTTP Basic
credentials the API enforces (entered by the user, cached only in `sessionStorage` for that
tab), looks up a consumer, and drives the recharge flow: amount + idempotency key in, live
wallet balance / transaction ledger / bill breakdown out, with inline success/pending/declined/
replayed states matching the API's actual response codes. Useful for demoing the recharge flow
without Swagger's non-scriptable native Basic-auth prompt getting in the way.

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
4. Verify: `dotnet run --project backend/PrepaidEngine.Api`, then `curl http://localhost:5299/health` → `{"status":"Healthy"}`, Swagger UI at `http://localhost:5299/swagger`, and the [demo console](#demo-console) at `http://localhost:5299/`.

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
| `GET /api/v1/bills` | HTTP Basic | Every bill across all consumers, joined with tariff/category — backs the Billing dashboard |
| `GET /api/v1/bills/{id}` | HTTP Basic | Full calculation trace for one bill — backs Bill Detail |
| `GET /api/v1/recharges` | HTTP Basic | Every recharge attempt across all consumers — backs Recharge Operations |
| `GET /api/v1/recharges/{id}` | HTTP Basic | Full recharge detail with current RMS wallet balance — backs Recharge Detail |
| `GET /api/v1/tariffs` | HTTP Basic | Every configured tariff — backs Tariffs & Rules |
| `GET /api/v1/tariffs/{id}` | HTTP Basic | One tariff's slabs, ToD periods, and vend limits — backs Tariff Detail |
| `POST /api/v1/calculation-workbench/simulate` | HTTP Basic | SIMULATION-ONLY charge preview for an arbitrary tariff/consumption/load — backs the Calculation Workbench |
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

**155 tests, all passing.** Breakdown:

| Test class | Count | What it covers |
|---|---|---|
| `TariffTests` | 12 | Slab energy charge, fixed charge, rebate composition, vend-amount validation |
| `TariffGoldenDataTests` | 9 | **Regression against real day-by-day rows from MePDCL's own reference workbook** — energy charge crossing slab boundaries, daily fixed-charge proration, full net-bill composition, including a zero-consumption day |
| `BplTariffTests` | 6 | BPL/Kutir Jyoti 4-slab tariff (special first-30-kWh rate + normal domestic slabs) |
| `DhtTariffDiscrepancyTests` | 2 | The documented DHT ₹5.85 (production) vs. ₹5.87 (legacy Excel reference) discrepancy — both kept as explicit, separately named tests, neither silently overriding the other |
| `ElectricityDutyTests` | 15 | Category-based duty: Domestic/BPL flat rate, "Others" flat rate, Industrial tiered slabs (including cumulative-position vs. raw-delta correctness), negative-input validation |
| `FppasChargeTests` | 10 | **Regression against both worked FPPAS examples in the reference workbook** — negative and positive rate cases, unrounded daily proration matching the workbook's exact precision, the paisa-accurate rounded variant, applicable-billing-month scheduling, input validation |
| `PrepaidBillTests` | 16 | Full charge-breakdown composition (energy net + fixed + duty + FPPAS + TMC + CPMC + arrears), arrears-first payment allocation (uncapped and capped), multi-installment arrears recovery, validation guards |
| `TransformerMaintenanceChargeTests` | 4 | TMC by voltage (11/33/132 kV), opt-in/opt-out, exclusive- vs. shared-use billing basis, negative-input validation |
| `CtPtMaintenanceChargeTests` | 7 | CPMC by voltage/wiring combination, opt-in/opt-out, undefined-132kV-rate handling |
| `ArrearRecoveryTests` | 11 | Uncapped arrears-first behavior (tariff book §13.4 default), optional caller-supplied recovery cap, 100%-cap-equivalence, input validation |
| `PrepaidWalletTests` | 12 | Wallet credit/debit, emergency-credit tracking, consumer connect/disconnect/reconnect rules |
| `MockRmsClientTests` | 9 | Recharge success/failed/pending/unavailable outcomes, idempotent replay, **20-way concurrent-call race test**, input validation, transaction-status lookup |
| `PrepaidEngineDbContextTests` | 6 | Real persistence round-trips against SQLite (keys, FKs, owned collections) — including a regression test for a real EF change-tracking bug found while building the recharge endpoint (crediting an already-loaded wallet), a `PrepaidBill`↔`FppasCharge` round-trip, and a `Tariff`↔`TouPeriod` round-trip (classify + charge calculation after reload) |
| `TouTariffTests` | 14 | Reproduces the exact IHT (5.55/6.66/4.72 kVAh) and IEHT (6.60/7.92/5.61 kVAh) ToD schedules from the tariff book — boundary transitions, midnight wraparound, unknown-label and negative-consumption validation, and the relaxed slabs-OR-ToD-periods constructor rule |
| `TouPeriodTests` | 15 | `Contains` boundary behavior for wrapping and non-wrapping periods, constructor validation (equal start/end, negative rate, empty label, time ≥ 24h) |

Every number in `TariffGoldenDataTests`, `BplTariffTests`, and `DhtTariffDiscrepancyTests` is
taken verbatim from MePDCL's tariff book or reference workbooks, not invented — a failure there
means a real divergence from the utility's own numbers, not a made-up expectation.

### Frontend

Angular 22 standalone-component app implementing the real API surface as an enterprise
operations UI — see [docs/frontend-scope.md](docs/frontend-scope.md) for exactly what's
real vs. an explicitly-labeled stub.

```bash
cd frontend
npm install
npm start        # serves at http://localhost:4200
npm run build    # production build
npm test         # vitest unit tests
```

The API must be running first (`dotnet run --project backend/PrepaidEngine.Api`) — the
frontend calls it directly at `http://localhost:5043` (see `src/environments/environment.ts`).
The API's Development-only CORS policy allows `http://localhost:4200` specifically (see
`Program.cs`); it is never enabled outside Development.

Built:
- **Design system** — centralized tokens (`src/styles/_tokens.scss`): a teal/cyan-first
  enterprise-utility palette (not a generic blue admin dashboard), spacing/radius/shadow/type
  scales, Inter typography. No component hardcodes a color.
- **Shell** — collapsible sidebar (all planned modules listed, unbuilt ones marked "Soon") +
  header with global search input (not yet wired to a search API) + sign-out.
- **Sign-in** (`/login`) — verifies the HTTP Basic credential against a real API call before
  caching it in `sessionStorage`, mirroring the static demo console's approach.
- **Overview** (`/overview`) — real consumer count and a derived low-credit count; every
  other KPI is explicitly labeled "Illustrative" rather than inventing a number.
- **Consumers** (`/consumers`) — real, API-backed consumer list with client-side search.
- **Consumer 360** (`/consumers/:accountNumber`) — the primary screen: RMS Wallet Balance kept
  visually and semantically distinct from engine-calculated charges/arrears/emergency credit
  (RMS remains the source of truth for the real wallet — see Scope above), full real bill
  breakdown, wallet ledger, and the complete recharge flow wired to every actual API response
  (200 success, 200 replayed, 202 pending, 402 declined, 503 unavailable, 400 invalid) —
  verified live in-browser against the real Postgres-backed API.
- **Billing** (`/billing`) — every bill across all consumers with real KPIs (generated/paid/
  pending/overdue counts, total charges) and search.
- **Bill Detail** (`/billing/:id`) — the full calculation trace for one bill, reachable from
  both the Billing table and Consumer 360's bill table.
- **Recharge Operations** (`/recharge`) — every recharge attempt across all consumers with
  real KPIs (success rate, per-status totals) and search.
- **Recharge Detail** (`/recharge/:id`) — the recharge shown as a workflow, with RMS payment
  confirmation kept distinct from meter credit ("Not modeled in this environment" rather than
  an implied or fabricated success — no meter-command domain exists yet).
- **Tariffs & Rules** (`/tariffs`) — the real tariff configuration this engine bills against.
- **Tariff Detail** (`/tariffs/:id`) — one tariff's slab table, ToD schedule (when configured),
  and vend limits. Read-only — no create/update endpoint exists, since a real tariff-change
  workflow needs versioning/effective-dating/approval this project hasn't built yet.
- **Calculation Workbench** (`/calculation-workbench`) — a SIMULATION-ONLY charge preview for
  an arbitrary tariff/consumption/load combination. The frontend never computes the numbers
  itself: the backend delegates to the exact same domain methods production billing uses.
- Every other sidebar module routes to an honest "not yet backed" stub, never a fake dashboard.

## Documentation

- [`docs/assumptions-and-security.md`](docs/assumptions-and-security.md) — what's verified
  against a real source vs. assumed; a running security checklist against what's actually
  implemented (not a blanket compliance claim).
- [`docs/tariff-validation-report.md`](docs/tariff-validation-report.md) — every tariff rule
  implemented, its source (tariff book / Excel / both), the production decision where sources
  disagree, and which test case verifies it.
