# Domain rules

Calculation and workflow rules for the prepaid domain, verified against MePDCL's tariff book and
reference workbooks (see [assumptions-and-security.md](assumptions-and-security.md) and
[tariff-validation-report.md](tariff-validation-report.md) for sourcing). For how the system is
put together, see [ARCHITECTURE.md](ARCHITECTURE.md).

## Domain model

| Entity | Purpose |
|---|---|
| `Consumer` | A prepaid consumer: account, meter, connected load, connection status, owns a `PrepaidWallet` |
| `SmartMeter` | Meter number, phase (single/three), cumulative reading |
| `Tariff` / `TariffSlab` | Category-scoped slab tariff: energy slabs, fixed charge, prepaid rebate %, emergency-credit limit, per-phase vend limits. **Immutable once created**; has a lifecycle `Status` (Active/Retired) and at most one Active row per name |
| `ElectricityDuty` | Statutory per-unit duty, category-based (Domestic/BPL flat, Industrial tiered, Others flat) — modeled separately from `Tariff` since it isn't tariff-plan-specific |
| `TransformerMaintenanceCharge` / `CtPtMaintenanceCharge` | Opt-in fixed monthly maintenance charges (TMC, CPMC) for consumer-owned transformers/CT-PT sets, by voltage and (for CPMC) wiring |
| `ArrearRecovery` | Applies a payment against outstanding arrears — uncapped/arrears-first (tariff book) by default, or an optional caller-supplied recovery cap (RFP-indicative only) |
| `TouPeriod` | A Time-of-Day rate band (Normal/Peak/Off-peak) on a `Tariff` — only IHT/IEHT tariffs populate these |
| `Zone` > `Circle` > `Division` > `SubDivision` > `Substation` > `Feeder` > `Dtr` | The supply-network hierarchy, one table per level with a unique code, a name and a link to its parent. A `Consumer` hangs off one `Dtr` (`Consumer.DtrId`, null until mapped), so its whole path is known from that single link and any report can show or filter by any level |
| `PrepaidWallet` / `WalletTransaction` | Balance + append-only ledger; tracks emergency-credit usage separately from the normal balance |
| `ConsumptionReading` | A metered consumption reading for a billing period |
| `PrepaidBill` | A generated bill with payment/status tracking (`Generated`/`Paid`/`PartiallyPaid`/`Overdue`/`Cancelled`) |
| `RechargeTransaction` | A recharge processed through RMS, with its own status lifecycle (`Initiated`/`Success`/`Failed`/`Reversed`) |
| `FppasCharge` | A notified FPPAS (Fuel and Power Purchase Adjustment Surcharge) rate change, deferred one billing month and prorated across every day of the following month |
| `MeterCommand` | (Also records the MDM external command id, response code and message.) A meter credit command — the step that actually updates the smart meter's available credit after RMS confirms a recharge, wired into the recharge endpoint. Deliberately a separate entity/lifecycle from `RechargeTransaction` (`Queued`/`Sent`/`Acknowledged`/`Failed`/`TimedOut`) — RMS confirming payment and the meter itself being credited are two different systems succeeding independently, and the domain model refuses to conflate them (see below) |
| `ConnectivityCommand` | A remote disconnect/reconnect command dispatched to a consumer's meter (`Disconnect`/`Reconnect`, with a mandatory `Reason`), wired into real disconnect/reconnect endpoints. Deliberately a separate entity/lifecycle from `Consumer.ConnectionStatus` (`Queued`/`Sent`/`Acknowledged`/`Failed`/`TimedOut`) — the consumer's status records local *intent*, this entity tracks whether the physical meter actually acted on it, the same command/acknowledgement split used for `MeterCommand` (see [RC/DC domain model](#rcdc-domain-model--wired-into-a-disconnectreconnect-workflow) below) |
| `ConversionRequest` | A postpaid→prepaid conversion request as pushed by RMS (AMISP integration requirement doc §1), wired into a real batch endpoint. Tracks the RMS payload (transaction/meter/consumer numbers, consumer type, initial reading, conversion date) and its own decision trail, separate from `Consumer.BillingMode`'s real-time fact (see [Prepaid conversion](#prepaid-conversion-billing-reconciliation-and-the-amisp-integration-requirement-doc) below) |
| `ReconciliationAdjustment` | A signed wallet adjustment pushed by RMS (AMISP spec §7-8) — a gap found in RMS's own reconciliation, or a credit owed the consumer — applied to the wallet like a recharge but tagged distinctly in the ledger |
| `OperationalException` | An auto-raised operator work item whenever a `MeterCommand`/`ConnectivityCommand` reaches `Failed`/`TimedOut` — never hand-entered, resolved only with a mandatory note |
| `AuditEntry` | An immutable, append-only log entry for a tracked operational/config change (RC/DC dispatch, conversion completion, reconciliation adjustment, tariff version) |
| `TariffVersion` | A manually recorded parameter change against a `Tariff` (field, old/new value, note, effective date). Independent of the approval workflow; approved changes are visible in the tariff's version lineage instead |
| `TariffChangeRequest` | A proposed tariff change moving Draft → PendingApproval → Scheduled → Activated (or Rejected/Cancelled). IT drafts and submits, Utility approves with a commencement date. Owns the proposed slabs/ToD periods and records who did what and when. Activation creates a new immutable `Tariff` and retires the previous one — see `ARCHITECTURE.md` §4.2 |
| `DailyLoadProfile` | The meter's daily consumption profile, created at the 00:00 hrs boundary — the sole driver of ongoing prepaid billing (see [DLP billing pipeline](#dlp-billing-pipeline-daily-load-profile) below) |
| `MeterBillingControl` | A real, clearable hold blocking actual (real, meter-driven) billing for one consumer/meter — raised/reactivated automatically when an incoming DLP's reading is lower than that meter's own previous-day closing reading (see [DLP billing pipeline](#dlp-billing-pipeline-daily-load-profile) below) |
| `PaymentModeChangeCommand` | The MDMS → HES → Meter → HES → MDMS command that actually switches a meter to prepaid mode during conversion — see [Postpaid → Prepaid conversion](#postpaid--prepaid-conversion-mdmshes-flow) below |
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

### DLP billing pipeline (Daily Load Profile)

> **This section previously described a two-tier LS/DLP pipeline** (a 30-minute Load Survey
> stream driving hourly wallet debits, reconciled daily against the authoritative Daily Load
> Profile via a signed settlement adjustment). **The LS half has since been removed** at the
> user's explicit request, in favor of the MDMS/HES-driven conversion flow described in
> [Postpaid → Prepaid conversion](#postpaid--prepaid-conversion-mdmshes-flow) below. DLP is now
> the **sole** driver of ongoing prepaid billing, split across **two daily stages** by receipt
> time — no more hourly debits, no more settlement math.

- **Validation on ingest, before anything is billed**: every incoming DLP's starting cumulative
  reading is checked against the *same meter's* previous day's closing reading. If it's lower —
  a negative-consumption sequence across the day boundary — the profile is rejected
  (`DailyProfileStatus.Rejected`) and a `MeterBillingControl` hold is raised/reactivated for that
  meter, blocking further DLP billing until an operator clears it
  (`POST /api/v1/meter-data/{meterId}/billing-hold/clear`, mandatory resolution note). A
  previous-day profile that was itself Rejected is never used as the baseline — its reading isn't
  trustworthy either. This is the DLP pipeline's own version of the old LS pipeline's negative-
  consumption guard, now the only thing standing between bad meter data and a negative bill.
- **Two-stage billing**, both tracked under their own `BillingRun` (`DlpStage1RunType`/
  `DlpStage2RunType`, unique per `BillingDate`, so neither stage can double-run for the same date):
  - **Stage 1 (8:30-9:30 AM)**: bills every prepaid consumer whose DLP for the previous day was
    received by 8:00 AM that morning — a direct real charge, no waiting.
  - **Stage 2 (12:30-1:30 PM)**: bills every consumer whose DLP arrived between 8:00 AM and
    12:00 PM (missed Stage 1's cutoff) — still a real charge — then posts a **provisional** charge
    (demo estimation: average of up to the previous 7 valid DLPs, `0` if none exist — never
    treating a missing day as zero consumption without saying so) for every consumer who still has
    no usable DLP for that date by the 12:00 PM cutoff. A Rejected DLP counts as "no usable DLP"
    for provisional purposes, but never gets a second (colliding) profile row created for the same
    `ConsumerId + MeterId + ProfileDate` — it waits for a real corrected re-ingest instead.
  - `DailyLoadProfile.ReceivedAt` (server-set at ingest, distinct from the meter/head-end's own
    `GeneratedAt`) is what the two stages actually bucket by.
- **Daily charge**: `ChargeAmount = tariff.CalculateEnergyCharge(DLP total kWh) − prepaid rebate +
  daily fixed charge`, debited once as a `WalletTransactionType.DailyDlpCharge` (reference
  `DLP:<profile-id>`, or `DLP-PROV:<profile-id>` for a provisional estimate).
- `MeterAssignment` records a physical meter replacement (old/new meter id, closing/opening
  readings, reason) — the point of this audit trail is that an old meter's cumulative reading is
  **never** compared against a new meter's cumulative reading (they're different physical
  meters); `Consumer.ReplaceMeter()` swaps the meter.
- `NotificationEvent` — queued (not sent) low-balance/emergency-credit/disconnection-eligible/
  provisional-billing/conversion-completed/auto-disconnected/auto-reconnected notices, raised
  automatically during daily processing, conversion, recharge, and reconciliation. This project
  has no real SMS gateway; marking one "Sent" would only ever mean "a real dispatcher would pick
  this up next", so it stays `Pending` here rather than faking delivery.
- A minimal `BillingProcessingWorker` background service polls once a minute and dispatches each
  stage within its own window — intentionally simple; a durable scheduler/retry-guaranteed job
  framework is a documented production follow-up, not attempted here.
- Endpoints: `POST /api/v1/meter-data/dlp`, `POST /api/v1/billing/daily/{date}/stage1`,
  `POST /api/v1/billing/daily/{date}/stage2` (both accept an optional `?cutoff=` to trigger
  manually/for the demo without waiting for the clock),
  `POST /api/v1/consumers/{consumerId}/meter-replacement`,
  `GET /api/v1/consumers/{consumerId}/notifications`,
  `GET /api/v1/meter-data/billing-holds`,
  `POST /api/v1/meter-data/{meterId}/billing-hold/clear`.

### MDMS data foundation (BP / LS / IP / Events / Alarms / energy validation)

**Phase 1 of a 4-phase enterprise hardening effort** (`phase-1-data-foundation`). DLP above
remains the **sole** daily billing driver — none of what follows bills anything. Each MDMS profile
type has one job:

| Source | Entity | Role | Never used for |
|---|---|---|---|
| DLP | `DailyLoadProfile` | Sole daily billing driver | — |
| BP | `RegisterReading` | Register/billing validation — cross-checks DLP against the meter's actual cumulative register | Billing |
| LS | `LoadSurveyInterval` | Consumption intelligence (load pattern, peak demand, depletion forecasting) — re-introduced *strictly non-billing* after the old hourly-LS-billing pipeline was removed earlier | Billing |
| IP | `InstantaneousReading` | Meter health (voltage/current/power/PF/frequency/relay) | Billing, consumption |
| Events | `MeterEvent` | Informational history (power/comm fail-restore, relay, clock) | Requires no acknowledgement |
| Alarms | `MeterAlarm` | Tamper/cover-open/reverse-energy/abnormality — always severity + acknowledge/resolve workflow | Kept in its own table, deliberately never merged with Events |

- **Ingestion is push-based**, matching the existing DLP convention — MDMS (or, until a real MDMS
  integration exists, any caller) `POST`s each profile type; there is no outbound adapter to build
  since this engine never calls out to MDMS. Every ingest endpoint is **idempotent**: a duplicate
  key (`ConsumerId+MeterId+Timestamp` for BP/LS, `MeterId+Timestamp` for IP,
  `MeterId+Code+Timestamp` for Events/Alarms) is detected before insert and reported back as
  `"Duplicate"` rather than silently re-processed or erroring.
- **Meter-swap boundary respected implicitly**: every one of these entities scopes readings to a
  specific `MeterId` (never derives "the consumer's meter" from a mutable pointer), the same
  pattern `DailyLoadProfile` already uses — a reading from a since-replaced physical meter can
  never be pulled into another meter's comparison. `MeterAssignment` remains the source of truth
  for meter-identity history.
- **Energy validation framework** (`EnergyValidationResult`, `IMeterDataIngestionService.
  EvaluateEnergyValidationAsync`): cross-checks whichever of BP/DLP/LS have data for a given
  consumer/meter/day (`BpVsDlp`, `LsVsDlp`, `BpVsLs`), scoring `Pass`/`Warning`/`Fail` against
  **configurable** tolerances (`appsettings.json` → `EnergyValidation:WarningTolerancePct` /
  `FailTolerancePct`, default 2%/5% — never hard-coded in the comparison logic itself).
  Re-evaluating the same consumer/meter/date/rule updates that row in place rather than
  duplicating it. This endpoint only *records* the comparison — a `Fail` becomes a candidate
  `MeterBillingControl` hold reason for the billing pipeline to act on explicitly, not an automatic
  hold by itself (that wiring is Phase 2 scope).
- **DLP completeness** (`GetDlpCompletenessAsync`, `GET /api/v1/meter-data/dlp-completeness?date=`):
  computed on demand (never stored) across every active prepaid consumer for a given date —
  `Complete` / `Provisional` / `Invalid` (Rejected) / `Duplicate` (more than one DLP row) /
  `Missing` (none at all).
- Endpoints: `POST`/`GET /api/v1/meter-data/bp`, `POST`/`GET /api/v1/meter-data/ls`,
  `POST /api/v1/meter-data/ip`, `GET /api/v1/meter-data/ip/latest`,
  `POST`/`GET /api/v1/meter-data/events`, `POST`/`GET /api/v1/meter-data/alarms`,
  `POST /api/v1/meter-data/alarms/{id}/acknowledge`, `POST /api/v1/meter-data/alarms/{id}/resolve`,
  `GET /api/v1/meter-data/dlp-completeness`, `POST`/`GET /api/v1/meter-data/energy-validation`.
- Tests: `PrepaidEngine.Tests/MeterData/MeterDataIngestionServiceTests.cs` — duplicate/idempotent
  ingest for BP/LS/Events, negative-reading rejection, interval-order rejection, alarm
  acknowledge→resolve lifecycle (and its guard against an empty resolution note), DLP completeness
  (Missing/Complete), energy validation (Pass/Fail/no-data-yet/re-evaluation-updates-in-place), and
  the meter-swap boundary (a reading under a different `MeterId` is never pulled into a
  comparison).

### SLA monitoring, risk indicators, and exception-center wiring (operational controls)

**Phase 3 of the 4-phase enterprise hardening effort** (`phase-3-controls-reporting`). Audited
reconciliation, the exception center, and reporting first: reconciliation is deliberately a
one-directional, RMS-pushed-adjustment model (see `ReconciliationAdjustment`'s own doc comment —
an earlier three-way-comparison design was explicitly reverted as wrong against the real AMISP
spec, so Phase 3 does not reintroduce it), and the existing exception center/reports were already
real. This phase adds the two genuinely missing control-plane pieces plus one piece of connective
wiring between Phase 1 and the exception center:

- **SLA monitoring** (`ISlaMonitoringService`, `GET /api/v1/sla`): real performance for DLP
  ingestion latency, daily billing-run duration, recharge completion, meter-credit acknowledgement,
  and RC/DC acknowledgement — every target **configurable** via `appsettings.json`'s
  `SlaMonitoring` section (never hard-coded), computed from timestamps these entities already
  record. A workflow with zero completed samples reports `SampleSize: 0` and `"Unavailable"`,
  never a fabricated percentage.
- **Revenue & Risk Indicators** (`RiskIndicatorsSummary`, `GET /api/v1/risk-indicators`):
  deliberately never a monetary "revenue protected" figure (no real system-of-record for one
  exists here) — real counts only: open exceptions, active billing holds, unresolved meter alarms,
  disconnected consumers, failed energy validations.
- **Energy-validation → exception wiring**: a `Fail` from Phase 1's
  `EvaluateEnergyValidationAsync` now automatically raises a real, visible `OperationalException`
  (new `OperationalExceptionSourceType.EnergyValidation` source) instead of being recorded and
  forgotten — one Open exception per result, not duplicated on re-evaluation.
- Tests: `PrepaidEngine.Tests/Sla/SlaMonitoringServiceTests.cs` (no-data/met/breach/provisional-
  exclusion/average-computation) and two new cases in `MeterDataIngestionServiceTests` (a Fail
  raises exactly one exception; re-evaluating while still Open never duplicates it). Verified live
  against the real dev database, including a genuine end-to-end Fail → exception → risk-indicator
  count chain.

### Prepaid → Postpaid conversion (reverse flow)

**Phase 2 of the 4-phase enterprise hardening effort** (`phase-2-prepaid-core`). The forward
Postpaid→Prepaid flow below was already real; this closes the gap the domain model had left open
since `Consumer.ConvertToPostpaid()` existed but nothing ever called it in production code.

- Unlike the forward direction, **RMS never pushes this** — there is no external system decision
  to wait on, so `ReverseConversionRequest` completes in one operator-authorized step (a mandatory
  `Reason` + `RequestedBy`) rather than the forward flow's Requested→Approved→Completed chain.
- **Conversion safety** (mirroring spec §2.11 for the forward direction): rejects a consumer
  already billed Postpaid (duplicate conversion), one with an open `MeterBillingControl` hold
  (open billing issue), one with a still-pending forward `ConversionRequest`
  (Requested/Approved — a conflicting in-flight conversion), or one with another reverse request
  already in progress. Each rejection is itself recorded (as a Rejected `ReverseConversionRequest`
  with a `DecisionNote`) and audited — never a silent no-op.
- Captures the meter's cumulative reading and the wallet's balance at the moment of conversion —
  the last two real facts about the consumer's prepaid life before RMS's postpaid billing cycle
  and this project's own DLP/wallet billing stop applying to them.
- Endpoints: `POST /api/v1/conversions/reverse`, `GET /api/v1/conversions/reverse`.
- Tests: `PrepaidEngine.Tests/Domain/ReverseConversionRequestTests.cs` — construction validation,
  completion, and the guards against completing/rejecting twice or rejecting without a note.
  Verified live end-to-end against the real dev database, including both successful conversion and
  the "already Postpaid" rejection on retry.

### Postpaid → Prepaid conversion (MDMS/HES flow)

RMS pushes a batch of conversion requests carrying its finalized parameter set — Consumer ID,
Meter Serial Number, Last Reading/Billing Date, Temporary Disconnection/Reconnection Date, Last
Bill FR kWh/kVAh/Maximum Demand, Current Balance/Outstanding Amount, Conversion Date, Meter
Status, Permanent/Non-Permanent flag, and FOA/DIA (RMS itself zeroes both once outstanding
exceeds Rs. 10,000 — enforced here as a constructor validation guard on `ConversionRequest`, not
computed). Each accepted item then dispatches a `PaymentModeChangeCommand` down the
MDMS → HES → Meter chain (mocked via `IPaymentModeChangeClient` — no real HES integration exists
yet); only once that command reaches `Acknowledged` does the endpoint:

1. Complete the `ConversionRequest` and flip the consumer to Prepaid
   (`Consumer.ConvertToPrepaid()`, never a raw property set).
2. Credit any FOA+DIA amount into the new prepaid wallet (`WalletTransactionType.ConversionFoaDiaCredit`).
3. Post the conversion's **opening charge** — consumption from the 1st of the conversion month
   (the pre-existing `InitialReading`) to the meter's reading at the moment of conversion
   (`PaymentModeChangeCommand.MeterReadingAtConversion`), billed at the consumer's tariff
   (`WalletTransactionType.ConversionOpeningCharge`). Ongoing prepaid billing then proceeds via the
   DLP pipeline above from the day after conversion.
4. Queue a `NotificationEventType.PrepaidConversionCompleted` "you are now prepaid" SMS.
5. Run the emergency-credit guard once against the newly-created wallet.

A Failed/TimedOut payment-mode-change command instead rejects the `ConversionRequest` with the
command's own error detail — the request stays `Requested` until acknowledgement succeeds
(`Approve()` only runs on the success path), so a rejection can always transition cleanly from
`Requested` rather than hitting `Reject()`'s guard against an already-`Approved` request.

### Emergency-credit auto disconnect/reconnect

`IEmergencyCreditGuard` (Infrastructure: `EmergencyCreditGuard`) is called after every wallet
mutation that can move a prepaid consumer's balance — a recharge, a reconciliation adjustment, or
the daily DLP charge:

- Balance falls to/below the emergency-credit limit while the consumer is Active → auto-dispatch
  a Disconnect `ConnectivityCommand` (reason `EmergencyCreditGuard.AutoDisconnectReason`) through
  the same `IConnectivityCommandClient` the manual RC/DC endpoints use, and queue an
  `AutoDisconnected` notification.
- Balance becomes positive again while the consumer is Disconnected **for exactly that reason** →
  auto-dispatch a Reconnect `ConnectivityCommand` and queue an `AutoReconnected` notification.

Deliberately does not apply the manual RC/DC endpoint's "Happy Hours" (9 AM-2 PM) dispatch window
— that window exists for operator-initiated actions, and a wallet crossing the emergency-credit
line is a system-triggered event with no such restriction in the source requirement.
- **Explicitly out of scope for this phase** (per the spec's own "known limitations" section,
  and this project's discipline of never building a fake version of something real):
  a formal VEE (validation/estimation/editing) service with configurable thresholds, a real SMS
  provider and delivery-callback dispatcher, a real HES/MDM adapter, late-arriving-data
  correction for an already-closed hour, wallet-mutation row-locking/optimistic-concurrency under
  concurrent workers, a utility timezone configuration (the daily 00:00 boundary uses UTC,
  matching how every other timestamp in this system is stored), and a holiday calendar.
