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
`GET /api/v1/consumers/{accountNumber}`, `POST .../recharge`, `POST .../disconnect`,
`POST .../reconnect`, `GET /api/v1/bills`, `GET /api/v1/bills/{id}`, `GET /api/v1/recharges`,
`GET /api/v1/recharges/{id}`, `GET /api/v1/meter-commands`, `GET /api/v1/meter-commands/{id}`,
`POST /api/v1/meter-commands/{id}/retry`, `GET /api/v1/connectivity-commands`,
`GET /api/v1/connectivity-commands/{id}`, `POST /api/v1/connectivity-commands/{id}/retry`,
`GET /api/v1/tariffs`, `GET /api/v1/tariffs/{id}`,
`POST /api/v1/calculation-workbench/simulate`):

- **Sign-in** (`/login`) — verifies the HTTP Basic credential against a real API call before
  caching it (same approach as the static demo console at `backend/PrepaidEngine.Api/wwwroot/index.html`).
- **Overview** (`/overview`) — real consumer count and a derived low-credit count from actual
  wallet/emergency-credit data; every other KPI is explicitly labeled "Illustrative".
- **Consumers** (`/consumers`) — the real consumer list with live RMS wallet balances.
- **Consumer 360** (`/consumers/:accountNumber`) — full real bill breakdown (energy, rebate,
  fixed, duty, FPPAS, TMC, CPMC, arrears), real wallet ledger, the **real recharge flow**
  end to end, handling every actual API response: 200 success, 200 replayed, 202 pending,
  402 declined, 503 unavailable, 400 invalid — and a **real RC/DC panel**: Disconnect/Reconnect
  buttons (only one shown at a time, based on the consumer's actual current
  `ConnectionStatus`), gated behind an explicit confirmation dialog, a live outcome banner
  showing the real command status, and a header badge reflecting all four real states
  (`Active`/`Disconnected`/`DisconnectionPending`/`ReconnectionPending`) — the last two render
  a "command pending acknowledgement" message rather than a Disconnect/Reconnect button, since
  neither action is valid mid-flight.
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
  confirmation shown as a distinct step from the real meter-credit outcome. `MeterCommand` is
  now wired into the recharge endpoint: on RMS Success, the endpoint dispatches a command
  through `IMeterCommandClient` (`MockMeterCommandClient`) and the response/UI report the real
  `Acknowledged`/`Failed`/`TimedOut` result — never a fabricated or inferred success. Consumer
  360's recharge outcome banner shows the same distinction. See the "No fake success states"
  rule below.
- **Meter Credit** (`/meter-credit`) — every meter command across all consumers, real KPIs
  (acknowledged/failed/timed-out/pending, retried count, success rate), and search.
- **Meter Credit Detail** (`/meter-credit/:id`) — the command as a workflow, cross-linked to
  its originating recharge, with a **genuine** Retry action (`POST .../retry`) for `Failed`/
  `TimedOut` commands — gated behind an explicit confirmation dialog, calls
  `MeterCommand.Retry()` server-side and re-dispatches through the same `IMeterCommandClient`
  the original attempt used. Never a UI-only status flip.
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
  disabled with a specific, honest reason (e.g. the RC/DC reports now say "ConnectivityCommand
  data exists but no daily-aggregation report endpoint is built yet", reflecting that the
  domain model is real even though the report itself isn't) rather than being silently omitted
  or built as fake pages.
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

Every other sidebar module (RC/DC, Conversion, Exceptions, Reconciliation, Automation Center,
Audit & Activity, System Health) routes to an honest placeholder page stating that no backing
domain model or API exists yet — never a fake dashboard with invented numbers presented as
real. The 12 not-yet-real reports in the Reports Center follow the same rule at the card level
rather than the page level.

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
it renders the real, separately-tracked meter-credit outcome (`Acknowledged`/`Failed`/
`TimedOut`) rather than implying a downstream acknowledgement that never happened. The same
discipline carries through Meter Credit Detail's workflow and its Retry action, which only ever
reports whatever `IMeterCommandClient` actually returns.

## Build order for what comes next

Continuing in the same phase order as the UI/UX specification: Billing + Bill Detail, Recharge
Operations + Recharge Detail, Tariffs & Rules + Tariff Detail, Calculation Workbench, and
Reports Center (with 2 of 14 reports real) are done. This phase (Reports) added no new backend
endpoints — both real reports are built entirely on `GET /api/v1/bills`,
`GET /api/v1/bills/{id}`, and `GET /api/v1/consumers/{accountNumber}`, which already existed.

**Meter credit is now wired end to end.** `IMeterCommandClient`/`MockMeterCommandClient`
(`backend/PrepaidEngine.Application/MeterCommands/`, `backend/PrepaidEngine.Infrastructure/
MeterCommands/`, mirroring `IRmsClient`/`MockRmsClient`) was added, and the recharge endpoint
now creates + dispatches a `MeterCommand` on every RMS `Success`, updating it to `Acknowledged`/
`Failed`/`TimedOut` based on the (mocked) response — all in the same request. Both recharge read
endpoints (`GET /api/v1/recharges`, `GET /api/v1/recharges/{id}`) now expose the real meter-
command status, and both frontend surfaces that show a recharge outcome (Recharge Detail,
Consumer 360's recharge banner) render it — never inferring "meter credited" from "RMS
confirmed". `RechargeTransaction` itself always stays `Success` once RMS confirms, regardless
of the meter-command outcome; a failed/timed-out meter command is a separate, real, and now-
visible operational problem, not something that un-confirms the recharge.

**The Meter Credit page is now built too.** `GET /api/v1/meter-commands` and
`GET /api/v1/meter-commands/{id}` back a dashboard (`/meter-credit`, real KPIs and search across
every command) and a detail page (`/meter-credit/:id`, the command as a workflow, cross-linked
to its originating recharge). `POST /api/v1/meter-commands/{id}/retry` is a genuine action —
gated behind an explicit confirmation dialog on the detail page, it calls
`MeterCommand.Retry()` server-side (rejecting with `409` if the command isn't `Failed`/
`TimedOut`) and re-dispatches through the same `IMeterCommandClient` the original attempt used,
never a fabricated status flip. Verified live: a `TimedOut` command retried through the UI
correctly flipped to `Acknowledged` with `RetryCount` incremented and fresh timestamps.

**RC/DC is now wired end to end.** `IConnectivityCommandClient`/`MockConnectivityCommandClient`
(`backend/PrepaidEngine.Application/Connectivity/`, `backend/PrepaidEngine.Infrastructure/
Connectivity/`, mirroring `IMeterCommandClient`/`MockMeterCommandClient` exactly, 9 tests) backs
two real operator actions: `POST /api/v1/consumers/{accountNumber}/disconnect` and `.../reconnect`.
Both require a non-empty `Reason`, both reject on the wrong starting `ConnectionStatus` (`409`),
and reconnect additionally rejects a non-positive wallet balance (`400`) *before* ever dispatching
to the meter — the same "don't dispatch a command reality would reject anyway" reasoning Meter
Credit already established. `GET /api/v1/connectivity-commands` and `.../{id}` expose the command
list/detail, and `POST /api/v1/connectivity-commands/{id}/retry` re-dispatches a `Failed`/
`TimedOut` command through `ConnectivityCommand.Retry()`. Consumer 360 got a real **RC/DC panel**
(Disconnect/Reconnect, one shown at a time based on actual `ConnectionStatus`, gated behind an
inline confirmation step, with a live outcome banner) — this is the wiring phase's UI, same as
Meter Credit's recharge-panel integration was for that phase; a dedicated RC/DC dashboard/detail
page (mirroring `/meter-credit`) has not been built yet.

**A real bug was found and fixed during this phase's code review**, the same way the
apiBaseUrl/auth-interceptor bug was found during an earlier phase: the retry endpoint checked the
reconnect-eligibility balance rule only implicitly, by gating `consumer.Reconnect()` on
`Wallet.Balance > 0` right before calling it — but it called `command.MarkAcknowledged()`
unconditionally first. A reconnect command that had gone `Failed`/`TimedOut` while the balance was
still positive, then got retried after a later bill drained the wallet to zero, would come back
`Acknowledged` from the meter yet silently fail to reconnect the consumer — `ConnectivityCommand`
says success, `Consumer.ConnectionStatus` stays stuck in `ReconnectionPending`, and nothing
explains why. Fixed by re-checking the balance precondition explicitly before calling
`command.Retry()`, returning `400` instead, mirroring the precondition already enforced at the
original `/reconnect` dispatch. This is exactly the kind of gap the intent-vs-acknowledgement
split exists to catch — and also exactly why time-sensitive preconditions need re-checking at
every dispatch point, not just the first one.

Next up, in spec order: build the RC/DC dashboard/detail pages (mirroring Meter Credit's), then
Conversion, Reconciliation, Exception, and Audit once their domain models exist, plus the
reports that depend on all of that data — including the now-unblocked `Day-wise RC/DC Report`s
and `Meter Credit Failure Report`. Each phase gets its own real backend support (or an explicit
mock clearly labeled as such) before its UI is built — never the reverse.
