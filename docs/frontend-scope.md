# Frontend scope — real vs. planned

The full Prepaid Engine UI/UX specification describes ~30 pages, 15+ services, 14 mandatory
reports, and a dozen operational modules (Meter Credit, RC/DC, Conversion, Exceptions,
Reconciliation, Automation, Audit, Tariff Management, ...). Building all of it in one pass
would mean inventing a shadow backend's worth of entities and mock data with nothing real
behind it. Instead, this frontend is built incrementally, one real slice at a time, matching
the same discipline used on the backend throughout this project: implement against a real API,
verify it, then extend.

## What's real today

Built against the actual `PrepaidEngine.Api` endpoints (`GET /api/v1/consumers`,
`GET /api/v1/consumers/{accountNumber}`, `POST .../recharge`, `GET /api/v1/bills`,
`GET /api/v1/bills/{id}`, `GET /api/v1/recharges`, `GET /api/v1/recharges/{id}`,
`GET /api/v1/tariffs`, `GET /api/v1/tariffs/{id}`,
`POST /api/v1/calculation-workbench/simulate`):

- **Sign-in** (`/login`) — verifies the HTTP Basic credential against a real API call before
  caching it (same approach as the static demo console at `backend/PrepaidEngine.Api/wwwroot/index.html`).
- **Overview** (`/overview`) — real consumer count and a derived low-credit count from actual
  wallet/emergency-credit data; every other KPI is explicitly labeled "Illustrative".
- **Consumers** (`/consumers`) — the real consumer list with live RMS wallet balances.
- **Consumer 360** (`/consumers/:accountNumber`) — full real bill breakdown (energy, rebate,
  fixed, duty, FPPAS, TMC, CPMC, arrears), real wallet ledger, and the **real recharge flow**
  end to end, handling every actual API response: 200 success, 200 replayed, 202 pending,
  402 declined, 503 unavailable, 400 invalid.
- **Billing** (`/billing`) — every bill ever generated across all consumers, joined with
  tariff/category, with real KPIs (bills generated, paid/pending/overdue counts, total
  charges) derived from the same data — nothing illustrative on this page.
- **Bill Detail** (`/billing/:id`) — the full calculation trace for one bill (consumption →
  tariff → gross energy → rebate → net energy → fixed → duty → FPPAS → TMC → CPMC → arrears →
  total), plus payment status, all from the real API. Reachable from both the Billing table
  and Consumer 360's bill table.
- **Recharge Operations** (`/recharge`) — every recharge attempt across all consumers with
  real KPIs (success rate, totals by status) computed from actual `RechargeTransaction` rows.
- **Recharge Detail** (`/recharge/:id`) — the recharge as a workflow, with RMS payment
  confirmation shown as a distinct step from meter credit (labeled "Not modeled in this
  environment" rather than implied or faked). A `MeterCommand` domain model now exists in the
  backend (`Queued`→`Sent`→`Acknowledged`/`Failed`/`TimedOut`, one per `RechargeTransaction`)
  but is not yet wired into the recharge endpoint or exposed via any API — this page's label
  will need updating once it is. See the "No fake success states" rule below.
- **Tariffs & Rules** (`/tariffs`) — the real tariff configuration this engine bills against
  (slabs, ToD periods where configured, prepaid rebate, fixed charge, emergency-credit limit,
  vend limits), sourced straight from `Tariff`/`TariffSlab`/`TouPeriod`.
- **Tariff Detail** (`/tariffs/:id`) — one tariff's full slab table, ToD schedule (hidden
  entirely when a tariff has none, e.g. Domestic/DLT), and vend limits. Read-only: there is no
  create/update endpoint, since a real tariff-change workflow needs versioning, effective
  dating, and approval that this project hasn't built — see "never silently overwrite an
  active tariff" in the original UI/UX request. Exposing a naive PUT would violate that rule,
  so nothing was built rather than something that violates it.
- **Calculation Workbench** (`/calculation-workbench`) — a SIMULATION-ONLY charge preview for
  an arbitrary tariff/consumption/load combination. Never touches a real consumer, bill, or
  wallet, and the frontend never computes the numbers itself: `POST
  /api/v1/calculation-workbench/simulate` delegates to the exact same domain methods
  (`Tariff.CalculateEnergyCharge`/`CalculateFixedCharge`/`CalculateDailyFixedCharge`,
  `ElectricityDuty.Calculate`) that production billing uses, enforcing the "frontend must not
  duplicate production billing logic" rule for real. Rejects ToD-only tariffs (IHT/IEHT) with
  an explicit error rather than silently returning a zero energy charge.
- **Reports Center** (`/reports`) — lists all 14 reports from the original UI/UX request's
  mandatory-reports section; only the 2 with a real data source are clickable, the rest render
  disabled with a specific, honest reason (e.g. "No RC/DC domain model exists yet") rather than
  being silently omitted or built as fake pages.
  - **Daily Billing Report** (`/reports/daily-billing`) — every real bill, filterable by date
    range/status/search, with a real summary (total consumers, billed/overdue counts, total
    charges) and CSV export. Deliberately omits a "total energy" KPI since `BillSummary` (the
    list endpoint) doesn't carry consumption — only Bill Detail does — and showing a wrong
    number would be worse than showing none.
  - **Individual Charge Calculation Report** (`/reports/charge-calculation`) — every real bill
    for one selected consumer, each with its full calculation trace (fetched via
    `GET /api/v1/bills/{id}` per bill, since the consumer-scoped bill list alone lacks
    consumption/reading data), plus CSV export.
  - CSV export (`shared/utils/csv-export.ts`) is genuinely functional — it serializes exactly
    the rows already on screen via a Blob download, not a placeholder button.

## What's a labeled stub

Every other sidebar module (Meter Credit, RC/DC, Conversion, Exceptions, Reconciliation,
Automation Center, Audit & Activity, System Health) routes to an honest placeholder page
stating that no backing domain model or API exists yet — never a fake dashboard with invented
numbers presented as real. The 12 not-yet-real reports in the Reports Center follow the same
rule at the card level rather than the page level.

## Design system

Centralized design tokens in `frontend/src/styles/_tokens.scss` (teal-first palette, spacing,
radius, shadow, typography scale) — no component hardcodes a color. Status semantics follow one
vocabulary everywhere via `pe-status-badge` (success/warning/danger/info/analytic/neutral, each
with a distinct glyph so status never depends on color alone).

## Financial labeling rule (non-negotiable)

RMS remains the system of record for the consumer's real wallet. The UI never calls an
engine-side figure "Wallet Balance" — only the RMS-sourced figure gets that label
("RMS Wallet Balance"); engine-calculated charges, arrears recovered, and emergency credit are
always shown as visually distinct, separately labeled tiles on Consumer 360.

## No fake success states

The recharge outcome banner distinguishes "RMS Confirmed" from a final "Recharge Completed" —
it explicitly notes that meter-credit orchestration isn't modeled in this environment, rather
than implying a downstream acknowledgement that never happened.

## Build order for what comes next

Continuing in the same phase order as the UI/UX specification: Billing + Bill Detail, Recharge
Operations + Recharge Detail, Tariffs & Rules + Tariff Detail, Calculation Workbench, and
Reports Center (with 2 of 14 reports real) are done. This phase (Reports) added no new backend
endpoints — both real reports are built entirely on `GET /api/v1/bills`,
`GET /api/v1/bills/{id}`, and `GET /api/v1/consumers/{accountNumber}`, which already existed.

The **`MeterCommand` domain model** (`backend/PrepaidEngine.Domain/Entities/MeterCommand.cs`,
`Queued`→`Sent`→`Acknowledged`/`Failed`/`TimedOut`, one per `RechargeTransaction`, 18 passing
tests) now exists, but is domain-only so far: no API endpoint exposes it, it isn't wired into
the recharge flow (no `MeterCommand` is created when a recharge succeeds), and no mock
meter-command client exists (mirroring `MockRmsClient`/`IRmsClient`). Building the Meter Credit
UI page is the next step once that wiring exists — never before it, to avoid a page that shows
either fabricated data or an empty always-`Queued` state that misrepresents reality.

Next up, in spec order: wire `MeterCommand` into the recharge endpoint + add its read
endpoint(s) + build the Meter Credit page, then the remaining modules (RC/DC, Conversion,
Reconciliation, Exception, Audit) once their domain models exist, plus the 12 reports that
depend on that data. Each phase gets its own real backend support (or an explicit mock clearly
labeled as such) before its UI is built — never the reverse.
