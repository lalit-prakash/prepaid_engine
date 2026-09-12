# Prepaid Engine

Prepaid Engine is a backend service responsible for managing the end-to-end prepaid billing
lifecycle for smart-meter consumers. It generates prepaid bills based on consumption and
tariff data, integrates with RMS for billing and recharge processing, and manages consumer
disconnection and reconnection workflows.

**Scope**: this repository is the Prepaid Engine only — not an MDMS, HES, AMI, SCADA, or meter
communication platform. RMS is the authoritative system of record for the consumer's real
financial wallet; the Prepaid Engine's own `PrepaidWallets`/`WalletTransactions` tables are a
working ledger for billing/recharge orchestration, not a competing wallet.

**Current status**: backend domain + persistence + a mock RMS integration + mock meter-command
and connectivity-command integrations + a small demo API + a minimal hand-built demo console +
an Angular enterprise-operations frontend covering the real API surface (Overview, Consumers/360
with a real RC/DC panel, Billing, Recharge Operations, Meter Credit, RC/DC, Conversion,
Reconciliation, Exceptions, Audit, Tariffs & Rules, Calculation Workbench, and 2 of 14 Reports),
all verified against real data (a live PostgreSQL database and MePDCL's own tariff book +
reference calculation workbooks). Meter credit is wired into the recharge flow end to end (see
[Meter credit domain model](#meter-credit-domain-model) below) with its own dashboard/detail
pages, including a genuine `Retry()` action. RC/DC is wired into a real disconnect/reconnect
workflow (see
[RC/DC domain model](#rcdc-domain-model--wired-into-a-disconnectreconnect-workflow) below) with
its own dashboard/detail pages too, mirroring Meter Credit's. Backend and frontend support now
also exist for prepaid conversion, billing reconciliation, operational exceptions, an audit
trail, and tariff version history, implementing sections 1-8 of MePDCL's own AMISP integration
requirement doc for the first two (see
[Prepaid conversion, billing reconciliation, and the AMISP integration requirement doc](#prepaid-conversion-billing-reconciliation-and-the-amisp-integration-requirement-doc)
below). The remaining frontend modules (Automation Center, System Health) still render an
explicit "not yet backed" stub rather than invented data — see
[docs/frontend-scope.md](docs/frontend-scope.md) for the real-vs-planned boundary.

## Structure

- `backend/` — .NET 8 solution (`PrepaidEngine.sln`)
  - `PrepaidEngine.Api` — ASP.NET Core Web API (entry point, demo endpoints, Basic auth, seeding)
  - `PrepaidEngine.Application` — use cases / integration ports (`IRmsClient`, `IMeterCommandClient`)
  - `PrepaidEngine.Domain` — core domain models and business rules, no external dependencies
  - `PrepaidEngine.Infrastructure` — EF Core persistence (PostgreSQL), mock RMS adapter, seed data
  - `PrepaidEngine.Tests` — xUnit test project (317 tests — see [Testing](#testing))
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
| `MeterCommand` | A meter credit command — the step that actually updates the smart meter's available credit after RMS confirms a recharge, wired into the recharge endpoint. Deliberately a separate entity/lifecycle from `RechargeTransaction` (`Queued`/`Sent`/`Acknowledged`/`Failed`/`TimedOut`) — RMS confirming payment and the meter itself being credited are two different systems succeeding independently, and the domain model refuses to conflate them (see below) |
| `ConnectivityCommand` | A remote disconnect/reconnect command dispatched to a consumer's meter (`Disconnect`/`Reconnect`, with a mandatory `Reason`), wired into real disconnect/reconnect endpoints. Deliberately a separate entity/lifecycle from `Consumer.ConnectionStatus` (`Queued`/`Sent`/`Acknowledged`/`Failed`/`TimedOut`) — the consumer's status records local *intent*, this entity tracks whether the physical meter actually acted on it, the same command/acknowledgement split used for `MeterCommand` (see [RC/DC domain model](#rcdc-domain-model--wired-into-a-disconnectreconnect-workflow) below) |
| `ConversionRequest` | A postpaid→prepaid conversion request as pushed by RMS (AMISP integration requirement doc §1), wired into a real batch endpoint. Tracks the RMS payload (transaction/meter/consumer numbers, consumer type, initial reading, conversion date) and its own decision trail, separate from `Consumer.BillingMode`'s real-time fact (see [Prepaid conversion](#prepaid-conversion-billing-reconciliation-and-the-amisp-integration-requirement-doc) below) |
| `ReconciliationAdjustment` | A signed wallet adjustment pushed by RMS (AMISP spec §7-8) — a gap found in RMS's own reconciliation, or a credit owed the consumer — applied to the wallet like a recharge but tagged distinctly in the ledger |
| `OperationalException` | An auto-raised operator work item whenever a `MeterCommand`/`ConnectivityCommand` reaches `Failed`/`TimedOut` — never hand-entered, resolved only with a mandatory note |
| `AuditEntry` | An immutable, append-only log entry for a tracked operational/config change (RC/DC dispatch, conversion completion, reconciliation adjustment, tariff version) |
| `TariffVersion` | A recorded parameter change against a `Tariff`, with a mandatory change note and effective date — enables a future Tariff Change Report even though `Tariff` itself has no update endpoint yet |
| `LoadSurveyInterval` | One 30-minute Load Survey block — drives hourly wallet debits. Deliberately never the same object as `DailyLoadProfile` (see [LS/DLP billing pipeline](#lsdlp-billing-pipeline-load-survey--daily-load-profile) below) |
| `DailyLoadProfile` | The meter's daily consumption profile, created at the 00:00 hrs boundary — drives the authoritative daily charge and its settlement against the day's LS debits |
| `MeterBillingControl` | An active hold blocking actual (real, meter-driven) billing for one consumer/meter after negative-consumption data is detected — never silently converted to zero consumption |
| `BillingRun` | An operational record of one daily DLP settlement batch, unique per `RunType + BillingDate` |
| `MeterAssignment` | An audit record of a physical meter replacement — the boundary that guarantees an old meter's cumulative reading is never compared against a new meter's |
| `NotificationEvent` | A queued (not sent) low-balance/emergency-credit/disconnection-eligible/provisional-billing notice — this project has no real SMS gateway |

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

### Meter credit domain model

`MeterCommand` models the step after RMS confirms a recharge payment: actually crediting the
smart meter's available credit — and is now **wired into the recharge endpoint**:

- On RMS `Success`, the endpoint creates a `MeterCommand`, marks it `Sent`, and dispatches it
  through `IMeterCommandClient` (`MockMeterCommandClient` for now — a real STS/DLMS/COSEM/vendor
  adapter would plug in behind the same interface, mirroring `IRmsClient`/`MockRmsClient`).
  The command transitions to `Acknowledged`, `Failed`, or `TimedOut` based on the (mocked)
  response, all within the same request/`SaveChangesAsync()`.
- One `MeterCommand` per `RechargeTransaction` (enforced by a unique index — retries reuse the
  same row via `Retry()`, they don't create a new one).
- Lifecycle: `Queued` → `Sent` → `Acknowledged` (the *only* state that means the meter was
  actually credited) — or `Sent` → `Failed`/`TimedOut`, either of which can `Retry()` back to
  `Queued` (incrementing `RetryCount`). **`RechargeTransaction` remains `Success` regardless of
  the meter-command outcome** — RMS already confirmed the payment; a rejected or unacknowledged
  meter command is a separate operational problem, not a reason to un-confirm the recharge.
- Enforces the "no fake success states" rule at the type level: `MarkAcknowledged()` can only be
  called after `MarkSent()`, and there is no way to reach `Acknowledged` from RMS confirmation
  alone — the two lifecycles (`RechargeStatus` and `MeterCommandStatus`) are entirely separate,
  and the recharge response/UI always report both.
- In `MockMeterCommandClient`, include `METERFAIL-` or `METERTIMEOUT-` anywhere in your
  recharge's `IdempotencyKey` to force the meter to reject or never acknowledge the credit
  (while RMS still confirms payment normally) — e.g. `METERFAIL-demo-001`.
- Exposed via `GET /api/v1/recharges` (list-level `meterCommandStatus`) and
  `GET /api/v1/recharges/{id}` (full `meterCommand` object: status, retry count, error, 
  timestamps) — both rendered on the frontend's Recharge Detail page and Consumer 360's
  recharge outcome banner.
- `GET /api/v1/meter-commands` / `GET /api/v1/meter-commands/{id}` expose every command
  across all consumers — real KPIs and search — backing the **Meter Credit** dashboard
  (`/meter-credit`) and detail (`/meter-credit/:id`) pages.
- `POST /api/v1/meter-commands/{id}/retry` is a **genuine** retry action, not a UI-only status
  flip: it calls `MeterCommand.Retry()` (rejecting with `409` if the command isn't
  `Failed`/`TimedOut`), re-dispatches through the same `IMeterCommandClient` the original
  attempt used, and persists whatever real outcome comes back. The Meter Credit Detail page
  gates it behind an explicit confirmation dialog (per the "critical command confirmation"
  pattern) and only shows the button for retryable commands.
- Meter Credit Detail and Recharge Detail cross-link to each other (a command's originating
  recharge, and a recharge's dispatched command), so an operator investigating either side of
  the RMS/meter split never loses context.

### RC/DC domain model — wired into a disconnect/reconnect workflow

`ConnectivityCommand` models a remote disconnect or reconnect command dispatched to a
consumer's meter, and is now wired into two real endpoints:

- Deliberately a separate entity/lifecycle from `Consumer.ConnectionStatus` — the consumer's
  own status (including its `DisconnectionPending`/`ReconnectionPending` values) records local
  *intent* (set immediately on request), while `ConnectivityCommand` tracks whether the
  physical meter actually acted on that intent. Identical split to `MeterCommand`/
  `RechargeTransaction`. The consumer's status only advances to its final `Disconnected`/
  `Active` value once the dispatched command reaches `Acknowledged` — never inferred from the
  command merely being sent.
- `CommandType` (`Disconnect`/`Reconnect`) is an explicit, stored fact — never inferred from
  context — matching the UI/UX request's concern that direction (e.g. Postpaid→Prepaid vs.
  Prepaid→Postpaid, or Reconnect vs. Disconnect) is exactly the kind of thing that gets
  silently reversed by accident if it's derived rather than recorded.
- `Reason` is mandatory, not optional metadata: `POST /api/v1/consumers/{accountNumber}/
  disconnect` and `.../reconnect` both reject a missing/blank reason with `400`.
- Lifecycle: `Queued` → `Sent` → `Acknowledged` (the *only* state that means the meter's
  physical connection actually changed) — or `Sent` → `Failed`/`TimedOut`, either of which can
  `Retry()` back to `Queued` (incrementing `RetryCount`) via `POST /api/v1/connectivity-commands/
  {id}/retry`. Uses its own `ConnectivityCommandStatus` enum rather than reusing
  `MeterCommandStatus`, even though the shape is identical — matching `RechargeStatus` vs.
  `MeterCommandStatus`'s precedent of never sharing an enum across unrelated lifecycles.
- Both endpoints validate the consumer's current state before dispatching anything: disconnect
  requires `Active` (else `409`), reconnect requires `Disconnected` **and** a positive wallet
  balance (else `409`/`400`, checked before ever involving the meter — dispatching a command
  `Consumer.Reconnect()` would just reject on acknowledgement serves no one). The retry endpoint
  re-checks that same balance precondition for a `Reconnect` command, since a command can sit
  `Failed`/`TimedOut` for a while and the balance that justified it originally may no longer
  hold — this was a real bug caught by code review and fixed before merging (see the commit).
- In `MockConnectivityCommandClient`, include `CONNFAIL-` or `CONNTIMEOUT-` anywhere in the
  disconnect/reconnect `Reason` (or an optional `CorrelationId`) to force the meter to reject or
  never acknowledge the command.
- Exposed via `GET /api/v1/connectivity-commands` and `GET /api/v1/connectivity-commands/{id}`,
  backing a real dashboard (`/rc-dc`) and detail page (`/rc-dc/:id`), mirroring Meter Credit's.
  Consumer 360 also has a real RC/DC panel: Disconnect/Reconnect buttons (gated behind an
  explicit confirmation dialog per the "critical command confirmation" rule), a live outcome
  banner, and the header's connection-status badge reflecting all four real states (`Active`/
  `Disconnected`/`DisconnectionPending`/`ReconnectionPending`).
- Unlike `MeterCommand` (at most one per recharge), a consumer can be disconnected and later
  reconnected any number of times over its lifetime — no uniqueness constraint on `ConsumerId`,
  only an index for lookup.
- Says nothing about the transport (STS/DLMS/COSEM/vendor API) — no such integration exists
  yet; a future real `IConnectivityCommandClient` adapter would plug in the same way a real
  `IMeterCommandClient` adapter would for meter credit.
- **"Happy Hours" window (spec-required):** `POST /api/v1/consumers/{accountNumber}/disconnect`
  and the retry endpoint (when retrying a `Disconnect` command) both reject with `400` outside
  9:00 AM-2:00 PM IST (a fixed UTC+5:30 offset, not the server's own timezone). The spec (see below) also exempts public holidays on the Nagaland
  State Govt calendar — this project has no holiday-calendar concept, so only the daily window is
  enforced; the holiday gap is a known, documented limitation, not silently ignored.

### Prepaid conversion, billing reconciliation, and the AMISP integration requirement doc

This project implements sections 1-8 of MePDCL's own
`Prepaid_Integration_Requirement_Document_ProposalFromAMISP_V1.1` (the AMISP integration
requirement doc RMS and this engine are meant to satisfy) for two workflows: RMS pushing
postpaid→prepaid conversion requests, and RMS pushing reconciliation adjustments against a
consumer's wallet. Both replace an earlier, generic guess at these two domain models built before
the real spec was available — the shapes below are the real RMS payloads, not invented ones.

**Conversion** (`ConversionRequest`, spec section 1 — "Prepaid conversion" — and section 2,
first-bill generation):
- `POST /api/v1/conversions` accepts a **batch** (spec: "pushed in an array") of conversion
  requests, each carrying `TransactionId`, `MeterSerialNumber`, `ConsumerNumber` (matched against
  `Consumer.AccountNumber`), `RequestType` (defaults `"PRE"`), `ConsumerType`
  (`Residential`/`Vip`/`Hospital`/`School`/`ShoppingComplex`/`Other`), `InitialReading` +
  `InitialReadingDateTime` (the post-paid bill's final reading), and `ConversionDate`. Each item
  is validated and applied independently — one bad item in a batch never fails the rest — and the
  response is the spec's own per-item shape: `TransactionId`, `ConsumerNumber`, `ResponseCode`
  (`Success`/`Fail`), `ResponseMessage`.
- A `Consumer.IsNetMeter` consumer is always rejected (spec: "If the consumer is NET meter
  consumer, then such meter shall not be converted to prepaid").
- The decision trail (`ConversionRequest`: `Requested` → `Approved`/`Rejected` → `Completed`) and
  the actual billing-mode change (`Consumer.ConvertToPrepaid()`, never a raw property set) are
  always two explicit calls, even though the real flow applies both within one request — the same
  intent/execution split used by RC/DC and meter credit.
- `GracePeriodEndDate` (5 working days, Mon-Fri, after `ConversionDate`) and the `-300` grace
  threshold (vs. the normal `-200`) are computed on the entity and surfaced on
  `GET /api/v1/consumers/{accountNumber}` as `IsWithinConversionGracePeriod` /
  `EffectiveDisconnectThreshold` — **advisory only**: this project has no automated credit-based
  disconnect trigger (disconnection here is always the explicit manual operator action described
  above), so there is nothing for the grace threshold to override automatically yet. Building a
  real auto-disconnect decision engine is out of this project's current scope.
- Real SMS delivery ("your CID is now in pre-paid mode...") is **not modeled** — this repo has no
  notification/SMS gateway abstraction, and per the "no fake success states" rule the conversion
  endpoint does not report an SMS as sent, only the real `Success`/`Fail` response.
- `GET /api/v1/conversions` and `GET /api/v1/conversions/{id}` expose the full decision trail.

**Billing reconciliation** (`ReconciliationAdjustment`, spec sections 6-8):
- The real direction: RMS reconciles AMISP's daily billing data against its own shadow monthly
  bill and pushes any gap (or a consumer credit, e.g. from a bill revision or meter swap) to
  AMISP as a **signed amount**. This is an inbound instruction from RMS — not a three-way
  RMS/engine/meter comparison AMISP computes itself (an earlier version of this entity did that
  comparison; it modeled the wrong direction against the real spec and has been replaced).
- `POST /api/v1/consumers/{accountNumber}/reconciliation-adjustments` applies the signed `Amount`
  to the wallet exactly like a recharge credit/debit (tagged `WalletTransactionType.Reconciliation`
  in the ledger so it's never confused with a real top-up), with a required `Reference` and
  `ReconciliationDate`. Unlike a recharge, the amount may be negative and has no Rs. 500 floor.
- `GET /api/v1/billing-reconciliation/daily-export?date=` exposes what AMISP would push to RMS
  per spec section 7 — but **only the fields this domain genuinely tracks**: `MeterReadingDate`,
  `BillNumber`, `AccountNumber`, `MeterNumber`, and 3 charge-code/amount pairs derived from real
  `PrepaidBill` fields (Energy, Fixed, and a `0`-valued Public Lighting placeholder — this system
  has no separate public-lighting charge component to report). The spec's 4 cumulative
  midnight-reading fields (import/export kWh for the reading date and the day after) are always
  `null` — **not fabricated** — because `ConsumptionReading` only stores a period delta, and this
  system has no export/feed-in (NET meter) reading concept at all. RMS's own shadow monthly-bill
  calculation is entirely outside this system and is not modeled here.
- `GET /api/v1/reconciliation-adjustments` and `.../{id}` expose the applied-adjustment history.

**Minimum recharge (spec section 6):** `POST /api/v1/consumers/{accountNumber}/recharge` now
rejects amounts under Rs. 500 with `400`. This floor applies only to genuine top-ups — a
reconciliation adjustment (above) is a separate endpoint and is never subject to it, matching the
spec's own distinction between a recharge and a reconciliation-mode entry.

**Operational exceptions and audit trail** (`OperationalException`, `AuditEntry` — not part of the
AMISP spec, but built alongside it): an `OperationalException` is raised automatically whenever a
`MeterCommand` or `ConnectivityCommand` reaches `Failed`/`TimedOut` (`GET /api/v1/exceptions`,
resolve via `POST .../resolve` with a mandatory note) — never hand-entered, so nothing that needs
operator attention can go unlisted. An `AuditEntry` is appended for every RC/DC dispatch/retry, a
completed conversion's billing-mode change, an applied reconciliation adjustment, and a recorded
tariff version (`GET /api/v1/audit-entries`, filterable by entity type and date range) — an
immutable, append-only log, never edited or deleted.

**Tariff version history** (`TariffVersion` — also not part of the AMISP spec): `Tariff` itself
still exposes only its single current version (no update endpoint exists), but
`POST /api/v1/tariffs/{id}/versions` and `GET /api/v1/tariffs/{id}/versions` let a parameter
change be recorded with a mandatory change note and effective date, enabling a real Tariff Change
Report once one is built.

### LS/DLP billing pipeline (Load Survey + Daily Load Profile)

Implements the "Prepaid_Engine_Full_RnD_and_Code_Change_Spec" (v2.0, 11-09-2026): the engine
treats Load Survey (LS, a 30-minute interval stream) and Daily Load Profile (DLP, the meter's
daily profile created at the 00:00 hrs boundary) as two **different** meter-data products —
never modeled as the same object — and the core financial rule they exist to enforce is: **the
wallet must never be debited twice for the same consumption.** LS drives hourly debits; DLP
drives the authoritative daily charge; the two are reconciled via one signed settlement
adjustment, never a blind full re-debit on top of what the hourly debits already posted.

- `LoadSurveyInterval` — one 30-minute block. Immutable and idempotent: unique per
  `MeterId + IntervalStart + IntervalEnd` (DB-enforced), and its wallet debit uses the reference
  `LS:<interval-id>` as a second idempotency layer — re-running the same hour is a safe no-op.
- **Negative consumption is a data-quality event, never silently zeroed.** If a block's
  cumulative reading is lower than the immediately preceding block **for the same physical
  meter**, the block is rejected (`LoadSurveyQuality.NegativeConsumption`) and a
  `MeterBillingControl` hold is created/reactivated for that meter — both hourly LS billing and
  daily DLP billing stop for it until the hold is cleared via
  `POST /api/v1/meter-data/{meterId}/billing-hold/clear`, which requires a mandatory resolution
  note (this project's established mandatory-reason discipline) and leaves an audit trail.
  `GET /api/v1/meter-data/billing-holds` (optionally `?activeOnly=false` for full history) gives
  an operator visibility into what's currently blocked before deciding to clear anything.
- `DailyLoadProfile` — unique per `ConsumerId + MeterId + ProfileDate`. If none arrives by the
  daily billing run, a **provisional** profile is created instead (spec's demo estimation:
  average of up to the previous 7 valid DLPs, `0` if none exist — never treating a missing day as
  zero consumption without saying so) and a provisional debit is posted; the historical
  provisional transaction is never edited once the real DLP later arrives — only settled against.
- **Daily settlement**: `Settlement = Authoritative DLP charge − that day's LS hourly debits −
  any provisional debit already posted`. Debits the wallet if positive, credits it if negative,
  posts nothing if exactly zero — verified against both of the spec's own worked examples.
  Tracked under one `BillingRun` per date (`RunType + BillingDate` unique), so a run cannot be
  accidentally duplicated for the same day.
- `MeterAssignment` records a physical meter replacement (old/new meter id, closing/opening
  readings, reason) — the point of this audit trail is that an old meter's cumulative reading is
  **never** compared against a new meter's cumulative reading (they're different physical
  meters); `Consumer.ReplaceMeter()` swaps the meter, and because every LS/DLP record already
  scopes to a specific `MeterId`, sequence-continuity checks naturally never cross that boundary.
- `NotificationEvent` — queued (not sent) low-balance/emergency-credit/disconnection-eligible/
  provisional-billing notices, raised automatically during hourly and daily processing. This
  project has no real SMS gateway; marking one "Sent" would only ever mean "a real dispatcher
  would pick this up next", so it stays `Pending` here rather than faking delivery.
- A minimal `BillingProcessingWorker` background service polls once a minute and processes the
  previous completed hour (and, at midnight, the previous day's DLP settlement) — intentionally
  simple, per the spec's own framing; a durable scheduler/retry-guaranteed job framework is a
  documented production follow-up, not attempted here.
- Endpoints: `POST /api/v1/meter-data/ls` (batch), `POST /api/v1/meter-data/dlp`,
  `POST /api/v1/billing/hourly?hourEndUtc=`, `POST /api/v1/billing/daily/{date}`,
  `POST /api/v1/consumers/{consumerId}/meter-replacement`,
  `GET /api/v1/consumers/{consumerId}/notifications`,
  `GET /api/v1/meter-data/billing-holds`,
  `POST /api/v1/meter-data/{meterId}/billing-hold/clear`.
- **Four real bugs found and fixed across this work** (several by live curl tests against the
  real Postgres-backed API, not just unit tests — this project's established "verify live"
  discipline caught what an all-in-one-DbContext unit test setup had masked): (1) a duplicate LS
  block was still being added to the DbContext before being marked rejected, tripping the very
  uniqueness constraint meant to catch it — fixed by not persisting the duplicate at all, since
  the original block is already stored. (2) The daily settlement's consumer query never loaded
  the `Meter` navigation, throwing a `NullReferenceException` the moment `consumer.Meter.Id` was
  evaluated against a freshly-queried (not already-tracked) consumer — invisible in the test
  suite because
  its SQLite fixture reuses one long-lived, already-tracked `DbContext` throughout each test, but
  immediate against the real API on a first request. Fixed by including `Meter` in that query.
  (3) The daily settlement's LS-debit lookup filtered by `WalletTransaction.OccurredAt` (real
  wall-clock post time) instead of which calendar day the underlying `LoadSurveyInterval` itself
  covers — silently wrong whenever hourly processing runs even slightly out of sync with the
  interval's own date. Fixed by matching LS debits back to intervals via `IntervalStart` date
  range instead. (4) The sequence-continuity and duplicate checks in `IngestLoadSurveyAsync` only
  queried already-*saved* rows — since a whole batch is saved once at the end, ingesting two
  blocks for the same meter in one request (the spec's own example payload) meant the second
  block's continuity check couldn't see the first block still sitting in the same call, and a
  genuine negative-consumption sequence within one batch went undetected (confirmed live: two
  blocks in one request both came back `Valid` when the second should have triggered a hold).
  Fixed by tracking each batch's own added-but-not-yet-saved intervals alongside the database
  query.
- **The operator API to clear a `MeterBillingControl` hold is now built** (a follow-up flagged
  when this section first landed): `GET /api/v1/meter-data/billing-holds` (active-only by
  default, `?activeOnly=false` for full history) and
  `POST /api/v1/meter-data/{meterId}/billing-hold/clear` (mandatory resolution note, audit trail,
  actual LS/DLP billing for the meter resumes the moment it clears) — plus a real frontend page,
  **Billing Holds** (`/billing-holds`): real KPIs (total/active/cleared), a searchable table, a
  toggle to include cleared history, and a genuine confirmed Clear action requiring the same
  mandatory note the endpoint enforces. Bug (4) above was found while verifying this live.
- **Explicitly out of scope for this phase** (per the spec's own "known limitations" section,
  and this project's discipline of never building a fake version of something real):
  a formal VEE (validation/estimation/editing) service with configurable thresholds, a real SMS
  provider and delivery-callback dispatcher, a real HES/MDM adapter, late-arriving-data
  correction for an already-closed hour, wallet-mutation row-locking/optimistic-concurrency under
  concurrent workers, a utility timezone configuration (the daily 00:00 boundary uses UTC,
  matching how every other timestamp in this system is stored), and a holiday calendar. Frontend
  pages for LS/DLP data itself and meter replacement history were also not built this phase.

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
| `GET /api/v1/recharges` | HTTP Basic | Every recharge attempt across all consumers, with each row's `meterCommandStatus` — backs Recharge Operations |
| `GET /api/v1/recharges/{id}` | HTTP Basic | Full recharge detail with current RMS wallet balance and the full `meterCommand` object (status, retries, error, timestamps) — backs Recharge Detail |
| `GET /api/v1/meter-commands` | HTTP Basic | Every meter credit command across all consumers, each traced back to its recharge — backs the Meter Credit dashboard |
| `GET /api/v1/meter-commands/{id}` | HTTP Basic | Full meter command detail plus its originating recharge — backs Meter Credit Detail |
| `POST /api/v1/meter-commands/{id}/retry` | HTTP Basic | Genuinely retries a `Failed`/`TimedOut` command (`409` otherwise) by resetting it and re-dispatching through `IMeterCommandClient` |
| `POST /api/v1/consumers/{accountNumber}/disconnect` | HTTP Basic | Real RC/DC disconnect: requires a `Reason`, `409` unless currently `Active`, dispatches a `ConnectivityCommand` and only sets `Disconnected` on real acknowledgement |
| `POST /api/v1/consumers/{accountNumber}/reconnect` | HTTP Basic | Real RC/DC reconnect: requires a `Reason`, `409` unless currently `Disconnected`, `400` if wallet balance isn't positive, otherwise same dispatch/acknowledgement discipline as disconnect |
| `GET /api/v1/connectivity-commands` | HTTP Basic | Every disconnect/reconnect command across all consumers |
| `GET /api/v1/connectivity-commands/{id}` | HTTP Basic | Full connectivity command detail plus the consumer's current connection status |
| `POST /api/v1/connectivity-commands/{id}/retry` | HTTP Basic | Genuinely retries a `Failed`/`TimedOut` command (`409` otherwise, `400` if a `Reconnect` retry's balance precondition no longer holds, `400` outside the 9AM-2PM Happy Hours window for a `Disconnect` retry) |
| `POST /api/v1/conversions` | HTTP Basic | Batch prepaid conversion requests from RMS (AMISP spec §1) — per-item `Success`/`Fail` response |
| `GET /api/v1/conversions` | HTTP Basic | Every conversion request's decision trail |
| `GET /api/v1/conversions/{id}` | HTTP Basic | Full conversion request detail plus the consumer's current billing mode |
| `POST /api/v1/consumers/{accountNumber}/reconciliation-adjustments` | HTTP Basic | Applies an RMS-pushed signed wallet adjustment (AMISP spec §7-8) |
| `GET /api/v1/reconciliation-adjustments` | HTTP Basic | Every applied reconciliation adjustment across all consumers |
| `GET /api/v1/reconciliation-adjustments/{id}` | HTTP Basic | Full reconciliation adjustment detail |
| `GET /api/v1/billing-reconciliation/daily-export` | HTTP Basic | Daily billing data AMISP would push to RMS (AMISP spec §7) — real fields only, unmodeled ones left `null` |
| `GET /api/v1/exceptions` | HTTP Basic | Every operational exception, auto-raised on a `Failed`/`TimedOut` command |
| `GET /api/v1/exceptions/{id}` | HTTP Basic | Full operational exception detail |
| `POST /api/v1/exceptions/{id}/resolve` | HTTP Basic | Resolves an exception with a mandatory note |
| `GET /api/v1/audit-entries` | HTTP Basic | The immutable audit log, filterable by entity type and date range |
| `POST /api/v1/tariffs/{id}/versions` | HTTP Basic | Records a tariff parameter change with a mandatory note |
| `GET /api/v1/tariffs/{id}/versions` | HTTP Basic | A tariff's recorded version history |
| `GET /api/v1/tariffs` | HTTP Basic | Every configured tariff — backs Tariffs & Rules |
| `GET /api/v1/tariffs/{id}` | HTTP Basic | One tariff's slabs, ToD periods, and vend limits — backs Tariff Detail |
| `POST /api/v1/calculation-workbench/simulate` | HTTP Basic | SIMULATION-ONLY charge preview for an arbitrary tariff/consumption/load — backs the Calculation Workbench |
| `POST /api/v1/meter-data/ls` | HTTP Basic | Ingests a batch of 30-minute LS blocks |
| `POST /api/v1/meter-data/dlp` | HTTP Basic | Ingests one Daily Load Profile (replaces a provisional profile if one exists) |
| `POST /api/v1/billing/hourly` | HTTP Basic | Processes every consumer's completed hour — idempotent, safe to re-run |
| `POST /api/v1/billing/daily/{date}` | HTTP Basic | Runs the daily DLP settlement for every prepaid consumer with an assigned tariff |
| `POST /api/v1/consumers/{consumerId}/meter-replacement` | HTTP Basic | Records a physical meter swap with the old/new meter audit trail |
| `GET /api/v1/consumers/{consumerId}/notifications` | HTTP Basic | A consumer's queued notification history |
| `GET /api/v1/meter-data/billing-holds` | HTTP Basic | Every `MeterBillingControl` hold (active-only by default, `?activeOnly=false` for full history) |
| `POST /api/v1/meter-data/{meterId}/billing-hold/clear` | HTTP Basic | Clears an active billing hold — requires a mandatory resolution note |
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

**317 tests, all passing.** Breakdown:

| Test class | Count | What it covers |
|---|---|---|
| `TariffTests` | 14 | Slab energy charge, fixed charge, rebate composition, vend-amount validation |
| `TariffGoldenDataTests` | 9 | **Regression against real day-by-day rows from MePDCL's own reference workbook** — energy charge crossing slab boundaries, daily fixed-charge proration, full net-bill composition, including a zero-consumption day |
| `BplTariffTests` | 6 | BPL/Kutir Jyoti 4-slab tariff (special first-30-kWh rate + normal domestic slabs) |
| `DhtTariffDiscrepancyTests` | 2 | The documented DHT ₹5.85 (production) vs. ₹5.87 (legacy Excel reference) discrepancy — both kept as explicit, separately named tests, neither silently overriding the other |
| `ElectricityDutyTests` | 14 | Category-based duty: Domestic/BPL flat rate, "Others" flat rate, Industrial tiered slabs (including cumulative-position vs. raw-delta correctness), negative-input validation |
| `FppasChargeTests` | 10 | **Regression against both worked FPPAS examples in the reference workbook** — negative and positive rate cases, unrounded daily proration matching the workbook's exact precision, the paisa-accurate rounded variant, applicable-billing-month scheduling, input validation |
| `PrepaidBillTests` | 16 | Full charge-breakdown composition (energy net + fixed + duty + FPPAS + TMC + CPMC + arrears), arrears-first payment allocation (uncapped and capped), multi-installment arrears recovery, validation guards |
| `TransformerMaintenanceChargeTests` | 8 | TMC by voltage (11/33/132 kV), opt-in/opt-out, exclusive- vs. shared-use billing basis, negative-input validation |
| `CtPtMaintenanceChargeTests` | 9 | CPMC by voltage/wiring combination, opt-in/opt-out, undefined-132kV-rate handling |
| `ArrearRecoveryTests` | 11 | Uncapped arrears-first behavior (tariff book §13.4 default), optional caller-supplied recovery cap, 100%-cap-equivalence, input validation |
| `PrepaidWalletTests` | 12 | Wallet credit/debit, emergency-credit tracking, consumer connect/disconnect/reconnect rules |
| `MockRmsClientTests` | 9 | Recharge success/failed/pending/unavailable outcomes, idempotent replay, **20-way concurrent-call race test**, input validation, transaction-status lookup |
| `PrepaidEngineDbContextTests` | 10 | Real persistence round-trips against SQLite (keys, FKs, owned collections) — including a regression test for a real EF change-tracking bug found while building the recharge endpoint (crediting an already-loaded wallet), a `PrepaidBill`↔`FppasCharge` round-trip, a `Tariff`↔`TouPeriod` round-trip (classify + charge calculation after reload), a `MeterCommand` lifecycle round-trip with a uniqueness constraint test (one command per recharge), and a `ConnectivityCommand` lifecycle round-trip confirming multiple commands *are* allowed per consumer |
| `TouTariffTests` | 14 | Reproduces the exact IHT (5.55/6.66/4.72 kVAh) and IEHT (6.60/7.92/5.61 kVAh) ToD schedules from the tariff book — boundary transitions, midnight wraparound, unknown-label and negative-consumption validation, and the relaxed slabs-OR-ToD-periods constructor rule |
| `TouPeriodTests` | 15 | `Contains` boundary behavior for wrapping and non-wrapping periods, constructor validation (equal start/end, negative rate, empty label, time ≥ 24h) |
| `MeterCommandTests` | 18 | Full lifecycle state-machine coverage: `Queued`→`Sent`→`Acknowledged`, `Sent`→`Failed`/`TimedOut`→`Retry()` (incrementing `RetryCount`, resetting error/sent state), every invalid transition guarded and tested (e.g. acknowledging a never-sent command, retrying an already-acknowledged one), non-positive credit amount validation |
| `MockMeterCommandClientTests` | 9 | `METERFAIL-`/`METERTIMEOUT-` correlation-id markers (case-insensitive, anywhere in the string) producing `Failed`/`TimedOut`, default success path, non-positive credit amount / null-request / cancellation validation |
| `ConnectivityCommandTests` | 21 | Full lifecycle state-machine coverage mirroring `MeterCommandTests`: `Queued`→`Sent`→`Acknowledged`, `Sent`→`Failed`/`TimedOut`→`Retry()`, every invalid transition guarded and tested, plus `Disconnect`/`Reconnect` type recording and mandatory-`Reason` validation (null/empty/whitespace) |
| `MockConnectivityCommandClientTests` | 9 | `CONNFAIL-`/`CONNTIMEOUT-` correlation-id/reason markers (case-insensitive, anywhere in the string) producing `Failed`/`TimedOut`, default success path, null-request / cancellation validation |
| `ConversionRequestTests` | 22 | Real AMISP-spec fields (`TransactionId`/`MeterSerialNumber`/`ConsumerNumber`/`ConsumerType`/`InitialReading`/`ConversionDate`), `RequestType` defaulting to `"PRE"`, the 5-working-day grace-period-end calculation (skipping weekends) and `IsWithinGracePeriod`, full `Requested`→`Approved`/`Rejected`→`Completed` lifecycle coverage, validation guards |
| `ConsumerConversionTests` | 5 | `Consumer.ConvertToPrepaid()`/`ConvertToPostpaid()` guard against converting to the billing mode already in effect |
| `ReconciliationAdjustmentTests` | 9 | Signed-amount recording (positive and negative), zero-amount rejection, mandatory `Reference`/`ConsumerNumber` validation |
| `OperationalExceptionTests` | 11 | `Open`→`Resolved` lifecycle, mandatory resolution note, every `OperationalExceptionSourceType` |
| `AuditEntryTests` | 14 | Immutable append-only construction, mandatory fields, optional old/new value and details recording |
| `TariffVersionTests` | 7 | Mandatory change note and field name, old/new value recording, effective-date tracking |
| `LoadSurveyIntervalTests` | 8 | 30-minute duration enforcement, negative-cumulative-reading validation, `Received`→`Validated`/`Rejected`→`Processed` lifecycle |
| `DailyLoadProfileTests` | 6 | Total-kWh computation, provisional-profile creation, `ReplaceWithActual` replacing a provisional profile's data and clearing the flag |
| `MeterBillingControlTests` | 7 | Blocked-by-default construction, mandatory block reason, `Clear`/`Reactivate` lifecycle |
| `BillingEngineServiceTests` | 13 | End-to-end against a real SQLite-backed `PrepaidEngineDbContext`: LS ingestion (valid/duplicate/negative-consumption-triggers-hold, including both within one ingestion batch — a real bug regression), hourly processing (debits once per block, idempotent re-run, skips a billing-held meter), daily settlement matching both of the spec's own worked examples (positive and negative settlement), missing-DLP provisional estimation, one-run-per-date enforcement, and meter replacement never comparing old/new cumulative readings |

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
  (200 success, 200 replayed, 202 pending, 402 declined, 503 unavailable, 400 invalid) — plus a
  real RC/DC panel (Disconnect/Reconnect, gated behind an explicit confirmation dialog, a live
  outcome banner, and a header badge reflecting all four real connection states) — verified
  live in-browser against the real Postgres-backed API.
- **Billing** (`/billing`) — every bill across all consumers with real KPIs (generated/paid/
  pending/overdue counts, total charges) and search.
- **Bill Detail** (`/billing/:id`) — the full calculation trace for one bill, reachable from
  both the Billing table and Consumer 360's bill table.
- **Recharge Operations** (`/recharge`) — every recharge attempt across all consumers with
  real KPIs (success rate, per-status totals) and search.
- **Recharge Detail** (`/recharge/:id`) — the recharge shown as a workflow, with RMS payment
  confirmation kept as a distinct step from the real meter-credit outcome (`Acknowledged`/
  `Failed`/`TimedOut`, with error detail and retry count) — never conflating "RMS confirmed" with
  "meter credited". Consumer 360's recharge outcome banner shows the same distinction inline.
- **Meter Credit** (`/meter-credit`) — every meter credit command across all consumers, real
  KPIs (acknowledged/failed/timed-out/pending counts, success rate, retried count), and search.
- **Meter Credit Detail** (`/meter-credit/:id`) — the command as a workflow (created → sent →
  acknowledged/failed/timed-out), cross-linked to its originating recharge, with a **genuine**
  Retry action for `Failed`/`TimedOut` commands — gated behind an explicit confirmation dialog,
  re-dispatches through the real `IMeterCommandClient`, never a fabricated status flip.
- **Conversion** (`/conversion`) — every RMS postpaid→prepaid conversion request (AMISP spec
  §1), real KPIs (completed/rejected/pending counts, success rate), and search. Read-only — RMS
  submits the batch itself; this UI shows the resulting decision trail, never triggers one.
- **Reconciliation** (`/reconciliation`) — every applied `ReconciliationAdjustment` (AMISP spec
  §7-8), real KPIs (total credited/debited), and a genuine "Apply a Reconciliation Adjustment"
  form that calls the real endpoint and changes the consumer's actual wallet balance.
- **Exceptions** (`/exceptions`) — every auto-raised `OperationalException`, real KPIs
  (open/resolved counts), and a **genuine** Resolve action gated behind a mandatory note.
- **Audit** (`/audit`) — the immutable audit log, filterable by entity type and free text.
  Read-only by design — an audit entry is never edited or deleted from the UI.
- **Billing Holds** (`/billing-holds`) — every `MeterBillingControl` hold (real KPIs, a searchable
  table, a toggle to include cleared history), auto-raised on a real negative-consumption Load
  Survey sequence, with a **genuine** confirmed Clear action requiring a mandatory resolution
  note — see the [LS/DLP billing pipeline](#lsdlp-billing-pipeline-load-survey--daily-load-profile)
  section above.
- **Tariffs & Rules** (`/tariffs`) — the real tariff configuration this engine bills against.
- **Tariff Detail** (`/tariffs/:id`) — one tariff's slab table, ToD schedule (when configured),
  vend limits, and its recorded **Version History** (mandatory change note + effective date per
  entry, if any changes have been logged). Read-only — no create/update endpoint exists, since a
  real tariff-change workflow needs versioning/effective-dating/approval this project hasn't
  built yet (version *history* can still be recorded independently, which is what this shows).
- **Calculation Workbench** (`/calculation-workbench`) — a SIMULATION-ONLY charge preview for
  an arbitrary tariff/consumption/load combination. The frontend never computes the numbers
  itself: the backend delegates to the exact same domain methods production billing uses.
- **Reports** (`/reports`) — lists all 14 mandatory reports from the UI/UX spec; only 2 are
  real and clickable (the rest are disabled with a specific reason, e.g. "No RC/DC domain
  model exists yet"):
  - **Daily Billing Report** (`/reports/daily-billing`) — real bills filterable by date/status/
    search, with a real summary and CSV export.
  - **Individual Charge Calculation Report** (`/reports/charge-calculation`) — every real bill
    for one consumer with its full calculation trace, plus CSV export.
- Every other sidebar module routes to an honest "not yet backed" stub, never a fake dashboard.

## Documentation

- [`docs/assumptions-and-security.md`](docs/assumptions-and-security.md) — what's verified
  against a real source vs. assumed; a running security checklist against what's actually
  implemented (not a blanket compliance claim).
- [`docs/tariff-validation-report.md`](docs/tariff-validation-report.md) — every tariff rule
  implemented, its source (tariff book / Excel / both), the production decision where sources
  disagree, and which test case verifies it.
