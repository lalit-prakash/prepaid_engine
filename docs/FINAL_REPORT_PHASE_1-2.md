# Final report — Phase 1 (Tariff Governance + MDM Recharge) and Phase 2 (UI/UX and integration polish)

Repository: https://github.com/lalit-prakash/prepaid_engine · Board: https://github.com/users/lalit-prakash/projects/5
State reported: `main` after PR #18. Architecture reference: [ARCHITECTURE.md](ARCHITECTURE.md).

Scope of change since the start of Phase 1: 27 commits, 117 files, about +13,100 / −3,000 lines, 13 merged
pull requests (#6, #8–#18, plus #7 demo data). Backend now has 32 entities, 91 endpoints, 15 migrations and
397 automated tests (364 before Phase 1, 391 after it, 397 now); the frontend has 32 page components.

## How to read this report
It states what exists and how it was checked, and is explicit where the master prompt asked for something
that was **not** delivered or only partly delivered. Part C, sections 5 and 7, list every such gap in one place.

---

## PART A — Phase 1: Tariff governance and MDM recharge (PR #6, hardened in PR #18)

### 1. Tariff workflow
`Draft → PendingApproval → Scheduled → Activated`, with `Rejected` (back to Draft for revision) and
`Cancelled` (added in the audit pass). The master prompt's separate APPROVED and SCHEDULED states are
folded: approval always requires and records a commencement date, so an approved change is immediately
Scheduled. Tariff rows themselves are `Active` or `Retired`.

### 2. Role model
Two roles, `IT` and `Utility`, extended additively onto the existing HTTP Basic handler (configured users
with roles; the original single-credential shape still works and is treated as IT).
- IT: create, edit draft, submit, revise rejected, cancel own Draft/Rejected.
- Utility: approve (with commencement date), reject (with reason), cancel Pending/Scheduled, trigger activation.

### 3. Approval model
Enforced on the **server**, independent of the UI: policies `ITRole`, `UtilityRole`, `TariffGovernanceRole`.
Self-approval is blocked twice (an IT credential cannot reach `approve`; `TariffChangeRequest.Approve`
rejects the submitter). Utility cannot edit submitted values; the only path back to IT is Reject with a reason.

### 4. Versioning and historical integrity
A `Tariff` row is immutable. An approved change creates a **new** `Tariff` and retires the old one, so a bill's
`TariffId` always points at the exact rates it was calculated with. This required no change to the billing
engine. Verified live: the 9 Sep bill still points at the retired tariff (₹90 fixed charge, ₹5.00 first slab)
after two later revisions. The bill detail shows the tariff version id, Active/Retired status and a per-slab
breakdown computed on the backend. `GET tariffs/{id}/lineage` returns the full version chain with
effective/retired times, approver and reason.

### 5. Commencement, activation and retirement
- Approval requires a commencement date not before the approval date.
- A `TariffActivationWorker` runs at startup and every minute and calls `TariffActivationService`.
- Each due request activates in its **own transaction**: retire old, create new, mark Activated, write
  `ACTIVATED`/`RETIRED` audit entries. Re-running is safe (a request can only activate once).
- A request that cannot activate (e.g. its superseded tariff is already retired) is reported and stays Scheduled;
  it never blocks other requests or creates a second Active tariff.
- Verified live: an approved change activated about 20 seconds later with no endpoint call.

### 6. Validation and conflict rules
- On submit: first slab starts at 0; no gaps/overlaps; only the last slab unbounded; non-negative rates; unique
  ToD labels and no overlapping ToD windows; min ≤ max vend; rebate 0–100; non-negative emergency credit.
  Failures return field-level errors and no approval request is created.
- On create, submit **and** approve: the revised tariff must still be Active; no other pending/scheduled request
  may target it; a proposed name may not clash with a different Active tariff (also enforced by a filtered unique
  index on Active names).

### 7. Audit
Every governance action is audited with the real actor: `TARIFF_CREATED`, `DRAFT_SAVED`, `SUBMITTED`,
`APPROVED`, `REJECTED`, `CANCELLED`, `ACTIVATED`, `RETIRED`. The audit log is append-only; no endpoint can edit or
delete an entry. The change-request page shows that request's audit trail.

### 8. APIs added or changed
`GET/POST tariff-change-requests`, `GET .../{id}` (proposed vs current), `PUT .../{id}/draft`,
`POST .../{id}/submit|approve|reject|cancel`, `POST .../activate-due` (Utility only, returns `{ activated, failed }`),
`GET tariffs?status=`, `GET tariffs/{id}/lineage`, `GET auth/whoami`. Tariff list/detail now expose lifecycle status.

### 9. Database changes
Migration `AddTariffGovernanceAndMdmCorrelation`: `TariffChangeRequests` (+ owned slab/ToD tables); `Tariffs.Status`
(default `Active`, so existing rows are correct); `MeterCommands.ExternalCommandId/ResponseCode/ResponseMessage`;
the `Tariffs.Name` unique index became a filtered unique index on Active rows. A first draft of this migration
defaulted the new column to an empty string; it was caught by a live check, rolled back and regenerated with an
explicit default. Later: `AddMeterDataTimeIndexes` (Phase 2).

### 10. Tests
24 domain tests for `TariffChangeRequest` (validation, self-approval, reject/resubmit, commencement rules,
activate/cancel), 3 for tariff lifecycle, and 6 for `TariffActivationService` (due vs future, idempotent re-run,
failure isolation, stale request). The 364 pre-existing tests were preserved; Phase 1 added 27 (391), and the audit pass added 6 (397).

### 11. MDM recharge architecture
Flow: RMS confirms payment → recharge transaction → wallet ledger credit → `MeterCommand` → `IMeterCommandClient`
(the MDM/HES adapter seam) → acknowledgement. The existing abstraction was **extended, not replaced**
(`IMeterCommandClient` already separated the meter step from `IRmsClient`). Payment status and meter-credit status are
separate everywhere; nothing is labelled "successful" until the meter acknowledges. No DLMS/COSEM method, OBIS
code, STS token format, endpoint or vendor payload is assumed; the adapter is a mock for local/UAT and says so.

### 12. Adapter, lifecycle and idempotency
- `MeterCommand`: Queued → Sent → Acknowledged, or Failed / TimedOut; `Retry()` reuses the same row.
- Stored for audit: command id, recharge id, retry count, status, timestamps, error, and (new) external command id,
  response code, response message.
- Idempotency: unique index on `RechargeTransactionId` (one command per recharge), unique `RmsReferenceId`, and a
  required caller idempotency key that replays the original result. This already existed and was verified, not rebuilt.

---

## PART B — Phase 2: UI/UX and integration polish (PRs #8–#17)

**Approach, stated honestly:** the existing Angular app already followed the reference layout (dark sidebar, KPI
strip, card grid) from earlier work. Phase 2 was therefore delivered as a **module-by-module functional and
data-honesty upgrade to spec §4–§17**, not a fresh visual redesign. The design system (tokens, status badge,
KPI card) was reused; one shared component (accessible SVG bar chart) was added.

### 13. Design system and shell
Existing tokens reused. New shared `pe-bar-chart` (grouped series, legend, "view data as a table" fallback).
Status is always text plus glyph, never colour alone. Analytics is enabled in the navigation; the misleading
"Generate Daily Billing" quick action was removed.

### 14. Dashboard
Attention Required now includes paid-but-not-credited recharges and pending/scheduled tariff changes, and every
item drills to its source. Recent Recharges show payment and meter-credit status as separate columns. A Tariff
Governance card shows live counts. System Health no longer hard-codes "5 / 5 services"; it reflects a live `/health`
probe and labels unmonitored services. Low Balance and Disconnected tiles drill into the filtered Consumers list.

### 15. Consumers (list and Consumer 360)
Server-searched, keyset-paginated list (`consumers/search`; prefix on account/meter/mobile, substring on name;
status and low-balance filters; total count). Detail is tabbed: Overview (profile, meter, wallet stats derived from
the ledger), Wallet, Billing, Recharge, Meter operations, Meter data, Timeline. Each tab loads only that
consumer's rows on first open.

### 16. Recharge and meter credit
`recharges/search` and `recharges/summary` (database aggregates). KPI tiles drill into filters; payment and
meter-credit are separate columns and filters (including "failed or timed out"). Recharge detail shows a "Payment
received — meter credit not completed" banner linking to the existing confirmed retry. Recharge and meter-credit
detail show the MDM command id, response code and response message.

### 17. Billing
`bills/search` and `bills/summary`. Bill detail shows the tariff version used, its Active/Retired status, and a
per-slab energy breakdown computed on the backend (shown only when it reconciles with the stored gross charge).

### 18. Tariffs
Active tariffs, change requests by status, retired tariffs in History, the create/edit form (with an
unsaved-changes guard), the Utility approval workspace (current vs proposed with a Difference column, approve with
commencement date, reject with reason, cancel with reason), version lineage on tariff detail, and a per-request audit trail.

### 19. Meter data
DLP, BP, LS, Events and Alarms are searched, filtered and paged on the server (`meter-data/{profile}/search`) through a
shared pager, replacing the old 500/1000-row caps that could hide older data. IP remains a latest-per-meter snapshot.
Time-ordered indexes were added by migration.

### 20. Reports, Audit, Analytics
- Reports: server-side aggregates with a 5,000-row cap and a truncation flag; one shared viewer for Daily Billing,
  Day-wise RC, DC and Recharge, and the two failure reports; CSV export of the rows on screen.
- Audit: `audit-entries/search` and `summary`; filters; expandable entry detail.
- Analytics: `analytics/overview` (consumption, recharge, billing/collection, commands, communication events, wallet
  distribution, tariff mix, exceptions) over a bounded date range, all aggregated in the database.

### 21. Accessibility
Text plus glyph status badges; keyboard-focusable clickable rows (`Enter` activates); labelled search/filter controls;
charts with a data-table alternative; tab roles on tab bars. **Not verified:** screen-reader behaviour, colour-contrast
audit, and layouts at 1366px, laptop and tablet widths (only the default desktop width was exercised).

### 22. Performance
Server-side search and keyset paging for the six high-volume lists; database-side `GROUP BY` for summaries, reports and
analytics; debounced search (300 ms); per-tab lazy loading on Consumer 360 and meter data; bounded ranges and row caps.
**Not measured:** no load test or p50/p95/p99 figures exist (see Part C, sections 5 and 7).

### 23. Audit pass (PR #18)
Automatic tariff activation, failure-isolated activation, governance conflict rules, cancel endpoint, UTC normalisation
of every `DateTime` (date-only inputs previously returned 500), real actors on user-initiated audit entries, removal of
the static demo console and other dead code, a test-only dependency advisory cleared, and the documentation consolidated
into `ARCHITECTURE.md`, `DOMAIN_RULES.md`, a current README and a rewritten security note.

---

## PART C — The seven required lists

### 1. Completed
Everything in Parts A and B, verified by: backend build, 397 tests, frontend production build, and live checks against a
real PostgreSQL database with seven seeded consumers (paging with no gaps or duplicates, filter results matching known
data, role checks returning 403, the worker activating a change unaided, browser checks of each module).

### 2. External dependencies
- **MDM/MDMS and HES**: real endpoints, authentication, command and acknowledgement contracts (§3, §4).
- **RMS**: production adapter for payments, conversions and reconciliation (currently a mock).
- **Identity provider** for real authentication (§7).
- **WFM** if field workflow integration is required (not started).
- A production PostgreSQL environment, secrets manager, and a durable job scheduler.

### 3. MDM API information still required
Base URL and environments; authentication method; the command-submission endpoint and HTTP method; request schema and the
field that carries the meter identity; how the recharge/credit value is expressed; the correlation field returned and
echoed; the status-query endpoint; the acknowledgement mechanism (callback vs polling) and its signature/authentication;
timeout, retry and rate-limit rules; error codes and their meaning; idempotency behaviour on the MDM side; whether the engine
talks to MDM only or also to HES directly.

### 4. MDM/HES command contract still required
The exact SET-RECHARGE (or equivalent) operation and its payload; any token or credit-format requirements; the command
lifecycle and terminal states MDM reports; how a duplicate command is treated; meter-eligibility rules (prepaid mode,
communication state); how a failed or timed-out command may be retried safely; and the acknowledgement payload format. None of
this was invented; the adapter interface is the plug-in point.

### 5. Known limitations
- The recharge API waits for the adapter call; there is no outbox or background command worker, and the spec's recharge
  lifecycle states (RECEIVED … COMPLETED) are not modelled.
- Retroactive-change protection for already-billed periods is not implemented beyond immutability of tariff rows; there is no
  overlap check by scope (category/phase/connection type), only per-name uniqueness of Active tariffs.
- The "illustrative tariff impact" preview for a proposed tariff was not built.
- Tariff model lacks code/version number, demand/minimum charge, taxes/surcharge, low-balance threshold and grace period.
- Several list endpoints still return every row, and the dashboard KPIs are still computed in the browser from the consumer list.
- Reports over 5,000 rows cannot be exported in full; there are no background report jobs.
- No Fresh/Delayed/Stale/Missing freshness indicator, no abnormal-consumption detection, no area/hierarchy data, no balance history.
- System Health, Service Requests, User Management, Roles & Permissions, Integrations and System Settings are placeholders.
- The batch conversions endpoint saves once at the end, so one failure mid-batch loses the whole batch.
- No billing batch/job model or progress screen.
- Frontend unit tests are a scaffold only; there are no API integration tests for the Phase 2 endpoints.
- Some Phase 2 items were verified only at the default desktop width.

### 6. UAT scenarios
1. **Happy path tariff change:** IT creates a revision of the Active tariff, submits with a reason; Utility approves with a
   future date; the change stays Scheduled and billing keeps using the old tariff; on the date the worker activates it, the old
   tariff shows Retired, and the lineage shows both versions.
2. **Rejection loop:** Utility rejects with a reason; IT revises and resubmits; the audit trail shows each step.
3. **Self-approval and role checks:** an IT credential calling approve gets 403; a Utility credential calling create gets 403.
4. **Conflicts:** a second request on a tariff that already has a pending one, and a new tariff reusing an Active name, are refused.
5. **Cancel:** cancel a Scheduled change (Utility) and a Draft (IT) with a reason; both are audited; a Scheduled change never activates.
6. **Historical bills:** generate a bill, change the tariff, confirm the earlier bill still shows the old tariff version and rates.
7. **Recharge success:** recharge; payment received, ledger credited, meter credit Acknowledged; timeline shows separate statuses.
8. **Payment ok, meter fails:** use the mock's `METERFAIL-` / `METERTIMEOUT-` key prefixes; the dashboard shows a Critical attention item,
   the recharge shows the "meter credit not completed" banner, and Retry from the meter-credit page succeeds.
9. **Duplicate recharge:** resubmit the same idempotency key; no second credit.
10. **RMS outcomes:** `FAIL-`, `PENDING-`, `UNAVAILABLE-` key prefixes give declined, pending and 503 without touching the wallet.
11. **Disconnect/reconnect:** with a reason; status only becomes final on acknowledgement; audit records the acting user.
12. **Search and paging:** search by account, meter, name and RMS reference; step through pages; clear filters; drill down from dashboard tiles.
13. **Date-only inputs:** approve with a date-only commencement; record a tariff version with a date-only effective date.
14. **Reports and analytics:** each report over a date range and with filters; totals match the Billing and Recharge screens; CSV export.
15. **Dependency down:** stop the API and confirm pages show an error with retry, and the dashboard health probe shows Down.

### 7. Production readiness gaps
- **Security:** replace Basic auth with token-based authentication (expiry, refresh, MFA); real RBAC beyond IT/Utility with per-action
  permissions (reconciliation, conversion and billing triggers are currently open to any signed-in user); rate limiting, request size
  limits, secure headers/HSTS; secrets manager.
- **Audit:** add actor role, correlation id and source/IP; audit login/logout/failed login; link entries to consumers.
- **Integration:** real MDM/HES and RMS adapters; recharge outbox and command worker; circuit breaker/backoff; inbox for duplicate callbacks.
- **Scale and reliability:** page or aggregate the remaining unbounded reads; durable scheduler for billing and tariff activation with retry
  guarantees; billing batch/job model; background report jobs; load testing from 10,000 to 1,000,000 consumers with failure injection.
- **Quality:** API integration tests against a real PostgreSQL, frontend tests, accessibility and responsive verification, splitting the
  ~3,900-line `Program.cs`, monitoring and structured logging.
- **Product:** System Health and Integrations pages, Service Requests, tariff fields and the UNDER_REVIEW step, network hierarchy,
  configurable low-balance threshold and balance history, data-freshness and anomaly detection.

Each item above is a card on the project board.
