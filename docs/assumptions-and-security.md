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
attempts a login id is locked for 15 minutes (HTTP 429 with `Retry-After` and `retryAfterSeconds`). Login ids are matched ignoring case and surrounding spaces.
**System settings.** Admin and IT users change a fixed list of thresholds and targets from System Settings (`/api/v1/settings`): the low balance threshold, the energy validation tolerances and the service level targets. Values are stored in `SystemSettings`, layered over appsettings by a configuration source that honours only the declared keys (so a stray row cannot override a secret or connection string), validated (range, whole numbers, failure tolerance not below warning tolerance), audited (SETTING_CHANGED / SETTING_RESET) and applied on the next request with no restart (services read them through `IOptionsSnapshot`). A blank value puts a setting back to its default. Sign-in session, lock policy and e-mail settings are deliberately not editable here (secrets or restart needed).
**User management.** Admin and IT users manage users under User Management (`/api/v1/users`): create, edit, deactivate / activate (never delete, so the audit trail keeps meaning), set a password, unlock. Users created there are stored in the `Users` table (PBKDF2 hash, login id unique ignoring case) and sit alongside the bootstrap users in configuration, which are listed but read-only. Guards: you cannot deactivate yourself or change your own role, and at least one active Admin or IT user always remains. A deactivated user cannot sign in or renew a session; an access token already issued runs out on its own (at most 30 minutes). Every change is audited (USER_CREATED / UPDATED / ACTIVATED / DEACTIVATED / PASSWORD_SET / UNLOCKED). Roles are fixed; `AccessPolicies` is the one list the API's policies are built from and the Roles & Permissions tab shows.
**Password reset.** `POST /auth/forgot-password` e-mails a 6-digit one-time code to the address in `DemoAuth:Users:n:Email`;
`POST /auth/reset-password` takes the code and a new password (8+ characters, upper and lower case, digit, symbol, not containing
the login id). The reply to a request is identical for unknown ids and ids without an address, so it cannot be used to find accounts.
A code lives 10 minutes, allows 5 wrong guesses, works once, and a newer request cancels it; at most 3 codes an hour and one a minute
per login id, and 5 calls a minute per IP. Only a salted hash of the code is stored, and requests, failures and completions are
audited. Users live in configuration, so a new password is stored in the database (`UserPasswordOverrides`) and takes precedence over
the configured hash; a completed reset also clears any lockout. Mail goes through SMTP (`Email:Host`, `Port`, `User`, `Password`,
`FromAddress`; keep the password in user-secrets). With no `Email:Host`, Development prints the message (including the code) to the API
console and any other environment reports that e-mail is not set up. Known limit: whoever controls the registered mailbox can reset the password. The signing key (`Jwt:Key`,
32+ characters) is a secret from user-secrets or the environment; outside Development the API refuses to
start without one. The browser keeps only the token, its expiry and the display name in `sessionStorage`
(never the password). Known limits: users come from configuration rather than a database, the lockout is
in memory per API instance, tokens cannot be revoked before they expire, and there is no MFA.

**Authorization.** Deny by default, enforced at startup (an earlier version of this check read the wrong endpoint list and silently checked nothing; it now reads the app's own route builder, fails if it finds no endpoints, and was proven against a deliberately unprotected route, which also caught `POST auth/refresh` lacking a named policy). Every `/api/v1` endpoint requires authentication (`/health`, `POST auth/login` and
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
| Report exports | OK (single instance) | Full exports are an `Operations`-only action (Admin, IT, Operator; ReadOnly and Utility cannot request or download), audited on request and on download, limited to 3 at once per person and 5,000,000 rows, and their files are deleted after 7 days. People see only their own exports (Admin and IT see all). CSV cells that a spreadsheet could run as a formula are defused. **Limit:** files sit on the building instance's disk and are not encrypted at rest; put the folder on an encrypted, access-controlled volume and share it if more than one instance runs. |
| Audit logging | OK (append-only) | Tariff governance, RC/DC dispatch and retry, conversion, reconciliation, exception resolution, tariff version records, billing-hold clearing, **recharges (completed, failed, pending), meter replacements, meter alarm acknowledge/resolve, manually triggered billing stages, network hierarchy imports and consumer mappings, and sign-in events (`LOGIN_SUCCEEDED`, `LOGIN_FAILED`, `LOGIN_BLOCKED`, `LOGOUT`)** are audited. Every entry saved during a request is stamped with the actor's **role**, the **source address** and a **correlation id** (from the request's `X-Correlation-Id` header when it is a plain 8-64 character id, otherwise generated, and echoed on the response and in log scopes), by one interceptor, so call sites cannot forget it. Alarm acknowledgement uses the signed-in user, never a name typed in the request. The log is append-only and the API has no way to edit or delete an entry. Limits: system actions (workers) carry no role or address; a failed sign-in records the login id that was typed; the audit table is not yet tamper-evident (no hash chain) and has no retention policy. |
| Transport security | Partial | HTTPS redirection everywhere and HSTS outside Development. Every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, `Cross-Origin-Opener-Policy` and `Cross-Origin-Resource-Policy`; `/api` responses also carry `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` and `Cache-Control: no-store`. The `Server` header is removed. CORS allows only the origins in `Security:AllowedOrigins` (Development defaults to `http://localhost:4200`; a wildcard is refused at startup). `AllowedHosts` is `localhost;127.0.0.1` and must be set to the real host names when deployed. **Still to do:** TLS itself (terminated by the host or proxy) and a Content-Security-Policy for the static Angular files, which must be set by whatever serves them. |
| Dependency vulnerabilities | Clean at last check | `dotnet list package --vulnerable --include-transitive` reports none. The test-only `SQLitePCLRaw` packages were updated to 2.1.13 to clear GHSA-2m69-gcr7-jv3q. Re-run periodically. |
| Unbounded reads | Known risk | Several list endpoints (`consumers`, `bills`, `meter-commands`, `conversions`, `exceptions`, `notifications`, `meter-replacements`) return every row (billing holds now have a paged `search`). Fine for the demo data set; must be paged or aggregated before real volumes. |
| Rate limiting and request size | OK (single instance) | Fixed-window limiter per client IP: 600 requests/minute across the API (`/health` exempt) and 10 sign-in attempts/minute on `POST auth/login`, on top of the per-account lockout. Over the limit the API answers 429 with `Retry-After`. Request bodies are capped at 5 MB (413 above that). Limiter state is in memory, so with several API instances each keeps its own count; a shared store comes with the scale work. Behind a reverse proxy set `Security:TrustForwardedHeaders` so the real client IP is used. |

**Bottom line:** this is a working system with real server-side enforcement of the governance workflow,
not one that is security-reviewed for production. The blocking items are real authentication and role
management, fuller audit fields, and paging or aggregating the remaining unbounded reads.
