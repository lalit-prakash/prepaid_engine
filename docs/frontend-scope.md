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
`GET /api/v1/consumers/{accountNumber}`, `POST .../recharge`):

- **Sign-in** (`/login`) — verifies the HTTP Basic credential against a real API call before
  caching it (same approach as the static demo console at `backend/PrepaidEngine.Api/wwwroot/index.html`).
- **Overview** (`/overview`) — real consumer count and a derived low-credit count from actual
  wallet/emergency-credit data; every other KPI is explicitly labeled "Illustrative".
- **Consumers** (`/consumers`) — the real consumer list with live RMS wallet balances.
- **Consumer 360** (`/consumers/:accountNumber`) — full real bill breakdown (energy, rebate,
  fixed, duty, FPPAS, TMC, CPMC, arrears), real wallet ledger, and the **real recharge flow**
  end to end, handling every actual API response: 200 success, 200 replayed, 202 pending,
  402 declined, 503 unavailable, 400 invalid.

## What's a labeled stub

Every other sidebar module (Billing, Recharge Operations, Meter Credit, RC/DC, Conversion,
Exceptions, Reconciliation, Tariffs & Rules, Calculation Workbench, Reports, Automation Center,
Audit & Activity, System Health) routes to an honest placeholder page stating that no backing
domain model or API exists yet — never a fake dashboard with invented numbers presented as real.

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

Continuing in the same phase order as the UI/UX specification, next up: Billing dashboard +
Bill Detail (both already have real backend data via the bills already on `ConsumerDetail`),
then Recharge Operations/Meter Credit once/if a domain model for meter commands exists,
then the remaining modules in spec order. Each phase gets its own real backend support (or an
explicit mock clearly labeled as such) before its UI is built — never the reverse.
