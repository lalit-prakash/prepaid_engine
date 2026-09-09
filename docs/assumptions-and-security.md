# Assumptions, regulatory sourcing, and security notes

This file tracks what's been verified against a real source vs. assumed, and a running
security checklist against what's actually implemented — rather than claiming broad
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

**Not yet modeled from this source** (explicitly open, not silently dropped): ToD/peak-off-peak
tariffs for Industrial HT/EHT (§A.2–A.3), delayed payment charges (§12), disconnection/
reconnection fee schedule (§14), "friendly credit hours" during which supply should not be cut
even at zero balance (§22.4), initial credit on meter installation (§22.7), FPPAS, TMC, CPMC,
arrear recovery, and non-communicating-meter estimated billing (algorithm documented in the
Excel workbooks but not yet implemented — see tariff-validation-report.md). These need
consumption-interval data (for ToD) or a billing/collections module we haven't built yet, not
just tariff fields.

**Not sourced at all — do not assume compliance**: CEA metering/cyber-security regulations,
RDSS/AMISP technical standards, BIS/IEC meter standards (IS 15959, IEC 62056/DLMS-COSEM,
etc.), DPDP Act data-handling rules. I have not verified current text of any of these against
an authoritative source in this session, and nothing in the codebase should be read as
claiming compliance with them. If MDM/HES integration or personal-data handling work starts,
these need to be checked against the actual current regulation text first — not inferred from
general knowledge, which can be stale or wrong for a specific dated notification.

**This project's stated scope** (per the project owner) is the Prepaid Engine only — not MDM
or HES — so protocol-level standards (DLMS/COSEM, IS 15959) are out of scope until that
changes.

## Security review (of what's actually built so far)

Scope: `PrepaidEngine.Domain`, `.Application`, `.Infrastructure`, `.Api` as of this commit —
domain entities, EF Core persistence, mock RMS adapter. Beyond `/health`, two **read-only**
demo endpoints exist (`GET /api/v1/consumers`, `GET /api/v1/consumers/{accountNumber}`),
protected by HTTP Basic auth (`BasicAuthenticationHandler`, credentials in
`DemoAuth:Username`/`DemoAuth:Password` via user-secrets, compared with
`CryptographicOperations.FixedTimeEquals`). This is a stop-gap for a local demo — Basic auth
sends credentials on every request (mitigated locally by HTTPS via `UseHttpsRedirection`, but
still a single shared password, no per-user identity, no token expiry, no lockout on repeated
failures) and is **not** a substitute for real auth (token/OIDC + per-user authorization,
rate limiting on the auth endpoint) before any shared or production exposure.

| Area | Status | Notes |
|---|---|---|
| SQL injection | Not applicable / mitigated | All data access goes through EF Core's parameterized LINQ; no raw SQL/string-concatenated queries anywhere in the codebase. |
| Secrets in source control | OK | `appsettings.json`'s `ConnectionStrings:PrepaidEngine` holds only a `Password=CHANGE_ME` placeholder. The real PostgreSQL password is stored via `dotnet user-secrets` (outside the repo, under `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`), not committed. Before any shared/production deployment, move this to an actual secrets manager (Azure Key Vault, AWS Secrets Manager, etc.) rather than user-secrets, which is dev-only. |
| Money precision | OK | All monetary fields use `decimal` with explicit `decimal(18,2)` column precision; no `float`/`double` in financial paths. |
| Recharge idempotency | OK | `RechargeTransactions.RmsReferenceId` has a unique index; `MockRmsClient` replays the original result for a repeated `IdempotencyKey` rather than reprocessing. A real RMS adapter must preserve this guarantee. |
| Input validation | OK for current scope | Entity constructors and mutators throw on invalid state (negative amounts, empty required strings, invalid slab/vend ranges, etc.) — validation lives in the domain, not scattered across callers. |
| Authentication / authorization | Basic auth on demo endpoints only | `/health` is intentionally open (health-probe convention). The two demo consumer endpoints require HTTP Basic auth with credentials from user-secrets; verified live: no credentials → 401, wrong password → 401, correct credentials → 200. Still a single shared password with no per-user identity, no lockout, no rate limiting — real token/OIDC auth is required before any shared or production exposure. |
| Audit logging | **Not implemented** | No audit trail yet for wallet credits/debits, tariff changes, or connection-status changes. Needed before this handles real consumer data. |
| Transport security | Partial | `app.UseHttpsRedirection()` is wired in `Program.cs`; no HSTS/security headers configured yet. |
| Dependency versions | Checked — 2 fixed, 1 accepted | Ran `dotnet list package --vulnerable --include-transitive`. Found 3 high-severity transitive advisories, all in `PrepaidEngine.Tests` only (never shipped): `System.Net.Http` 4.3.0 and `System.Text.RegularExpressions` 4.3.0 — fixed by pinning to 4.3.4/4.3.1. `SQLitePCLRaw.lib.e_sqlite3` 2.1.6 (GHSA-2m69-gcr7-jv3q) — tried 2.1.10 and 2.1.11 (latest available on NuGet at time of check), both still flagged; no patched version exists upstream yet. Accepted as a known, unresolved, test-only risk — re-check when a fixed version ships. |

**Bottom line**: nothing here should be read as "security reviewed and cleared for
production" — it's an honest snapshot of a codebase with no exposed API surface yet. The two
concrete action items before any real endpoint ships are **authentication/authorization** and
**audit logging** on financial/state-changing operations.
