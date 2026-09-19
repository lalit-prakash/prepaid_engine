# Assumptions, regulatory sourcing, and security notes

This file tracks what has been verified against a real source vs. assumed, and a security
checklist against what is actually implemented — rather than claiming broad
"RDSS/industry-standard compliance" without evidence.

## Regulatory / tariff sourcing

**Verified source**: the MePDCL (Meghalaya Power Distribution Corporation) Electricity
Distribution Tariff booklet, effective 1 April 2026, supplied directly by the project owner.
Concepts and figures taken from it and reflected in the domain model:

| Concept | Source in tariff book | Where modeled |
|---|---|---|
| Slab-based domestic energy charge (₹5.00/5.04/5.10 per kWh) | §A.1 | `TariffSlab` |
| Fixed charge per kW/kVA per month | §A.1–A.3 | `Tariff.FixedChargePerUnitPerMonth` |
| 2% prepaid energy-charge rebate | §22.1 | `Tariff.PrepaidEnergyRebatePercent` |
| Emergency credit (₹200 / ₹2,000 for General Purpose) | §22.5 | `Tariff.EmergencyCreditLimit`, `PrepaidWallet.EmergencyCreditLimit` |
| Min/max recharge (vend) amount by meter phase | §22.6 | `Tariff.{Min,Max}VendAmount{SinglePhase,ThreePhase}` |
| No load security deposit for prepaid consumers | §1.3.5(a) | not yet modeled (no `SecurityDeposit` entity) |
| Consumer categories (Domestic, Non-Domestic, Industrial, etc.) | §II | `ConsumerCategory` enum |

**Also verified** against MePDCL's own reference calculation workbooks (`Prepaid bill
calculation.xlsx`, `Prepaid Calculation Category wise.xlsx`) — including a real, documented
discrepancy (DHT: Excel ₹5.87/kVAh vs. tariff book ₹5.85/kVAh, tariff book governs production)
and a day-by-day golden dataset used for regression tests. Full detail in
[tariff-validation-report.md](tariff-validation-report.md), including electricity duty
(`ElectricityDuty`), the daily fixed-charge proration formula and cumulative-differencing
energy charge (`Tariff.CalculateDailyFixedCharge` / `CalculateEnergyChargeForPeriod`), and BPL
slabs.

**Modeled since the first tariff pass:** ToD tariffs for Industrial HT/EHT, FPPAS, TMC and CPMC
maintenance charges, arrear recovery, and the two-stage DLP billing pipeline (see
[DOMAIN_RULES.md](DOMAIN_RULES.md)).

**Still not modeled from this source** (explicitly open, not silently dropped): delayed payment
charges (§12), the disconnection/reconnection fee schedule (§14), "friendly credit hours" during which
supply should not be cut even at zero balance (§22.4), initial credit on meter installation (§22.7),
the load security deposit exemption entity, and non-communicating-meter estimated billing (documented in
the Excel workbooks, not implemented — see tariff-validation-report.md).

**Not sourced at all — do not assume compliance**: CEA metering/cyber-security regulations,
RDSS/AMISP technical standards, BIS/IEC meter standards (IS 15959, IEC 62056/DLMS-COSEM, etc.), and
DPDP Act data-handling rules. None has been checked against an authoritative source, and nothing in the
codebase should be read as claiming compliance. If MDM/HES integration or personal-data handling work
goes further, check the actual current text first.

**Scope** (per the project owner) is the Prepaid Engine only — not MDM or HES — so protocol-level
standards (DLMS/COSEM, IS 15959) are out of scope. The MDM/HES adapters are mocks; no OBIS code, DLMS
method, STS token format or vendor payload is assumed anywhere.

## Security review (of what is actually built)

**Authentication.** JWT bearer (`backend/PrepaidEngine.Api/Auth`). `POST /api/v1/auth/login` checks the login
id and password and returns an HS256 token (default 30 minutes; `POST /auth/refresh` renews it until 8 hours
after the original sign-in). Passwords are stored as PBKDF2-SHA256 hashes (210,000 iterations, per-user salt)
and compared in constant time; an unknown login id costs the same as a wrong password. After 5 failed
attempts a login id is locked for 15 minutes (HTTP 429 with `Retry-After`). The signing key (`Jwt:Key`,
32+ characters) is a secret from user-secrets or the environment; outside Development the API refuses to
start without one. The browser keeps only the token, its expiry and the display name in `sessionStorage`
(never the password). Known limits: users come from configuration rather than a database, the lockout is
in memory per API instance, tokens cannot be revoked before they expire, and there is no MFA.

**Authorization.** Deny by default. Every `/api/v1` endpoint requires authentication (`/health`, `POST auth/login` and
Swagger in Development aside), and **every write endpoint (POST/PUT/PATCH/DELETE) must name an authorization
policy or the API refuses to start**. Five roles: `Admin`, `IT`, `Operator`, `Utility`, `ReadOnly`.

| Capability (policy) | Admin | IT | Operator | Utility | ReadOnly |
|---|:-:|:-:|:-:|:-:|:-:|
| Read every screen (any signed-in user) | yes | yes | yes | yes | yes |
| Operations: recharge, disconnect/reconnect, retries, conversions, reconciliation, exceptions, meter replacement, billing holds, alarm ack/resolve (`Operations`) | yes | yes | yes | no | no |
| Bulk meter data ingestion and billing runs (`DataAdmin`) | yes | yes | no | no | no |
| Tariff drafting: create, edit, submit, version records (`ITRole`) | yes | yes | no | no | no |
| Tariff approve, reject, activate (`UtilityRole`) | no | no | no | yes | no |
| Cancel a tariff request (`TariffGovernanceRole`) | yes | yes | no | yes | no |

Admin cannot approve tariffs on purpose: approval stays with `Utility`, and the approver may not be the
submitter. The UI hides actions a role cannot use, but the API is the boundary and answers 403. Verified
against a running API for ReadOnly, Operator, Utility and Admin.

| Area | Status | Notes |
|---|---|---|
| SQL injection | Mitigated | All data access uses EF Core parameterised LINQ. Search text is escaped for `LIKE` wildcards, so `%` and `_` match literally. |
| Secrets in source | OK | `appsettings.json` holds only a `CHANGE_ME` connection-string placeholder and no user credentials or signing key; real values are in user-secrets (dev-only — use a secrets manager for any shared deployment). |
| Money precision | OK | Money is `decimal(18,2)`; no floating point in financial paths. |
| Recharge / meter-credit idempotency | OK | Unique index on `RechargeTransactions.RmsReferenceId`; one `MeterCommand` per recharge; required caller idempotency key. A real RMS adapter must keep this guarantee. |
| Historical bill integrity | OK | Tariff rows are immutable; a bill keeps the tariff it used. |
| Input validation | OK for current scope | Domain entities throw on invalid state; tariff submission runs full structural validation; date-only inputs are normalised to UTC in the persistence layer. |
| Audit logging | Partial | Tariff governance, RC/DC dispatch and retry, conversion, reconciliation, exception resolution, tariff version records and billing-hold clearing are audited with the acting user. **Missing:** actor role, correlation id, source/IP, and login/logout events (failed and successful sign-ins are written to the application log, not the audit table). The log is append-only and the API has no way to edit or delete an entry. |
| Transport security | Partial | `UseHttpsRedirection` is wired; no HSTS or other security headers. CORS is limited to `http://localhost:4200` and only in Development. |
| Dependency vulnerabilities | Clean at last check | `dotnet list package --vulnerable --include-transitive` reports none. The test-only `SQLitePCLRaw` packages were updated to 2.1.13 to clear GHSA-2m69-gcr7-jv3q. Re-run periodically. |
| Unbounded reads | Known risk | Several list endpoints (`consumers`, `bills`, `meter-commands`, `conversions`, `exceptions`, `notifications`, `billing-holds`, `meter-replacements`) return every row. Fine for the demo data set; must be paged or aggregated before real volumes. |
| Rate limiting, request size limits, secure headers | Not implemented | Needed before any production exposure. |

**Bottom line:** this is a working system with real server-side enforcement of the governance workflow,
not one that is security-reviewed for production. The blocking items are real authentication and role
management, fuller audit fields, and paging or aggregating the remaining unbounded reads.
