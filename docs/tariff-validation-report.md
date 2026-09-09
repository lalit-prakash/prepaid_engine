# Tariff validation report

Sources actually inspected in this repository:

1. **Primary**: MePDCL Electricity Distribution Tariff, effective 1 April 2026 (PDF supplied by
   the project owner).
2. **Secondary**: `Prepaid bill calculation.xlsx` and `Prepaid Calculation Category wise.xlsx`
   (MePDCL reference calculation workbooks, supplied by the project owner).

Per the project owner's explicit instruction: where the tariff book and the Excel workbooks
disagree, **the tariff book is the production value**; the Excel value is retained only as a
named legacy/reference test case, never silently substituted.

## Verified rules and where they're implemented

| Rule | Excel value | Tariff book value | Production value | Decision | Test case |
|---|---|---|---|---|---|
| DLT energy slabs | 5.00 / 5.04 / 5.10 per kWh (0–100/100–200/200+) | same | same | No conflict | `TariffGoldenDataTests` |
| DLT fixed charge | ₹90/kW/month, daily = rate × load × 12/365 | ₹90/kW/month (proration not stated) | ₹90/kW/month; daily formula taken from Excel | Tariff book gives the monthly rate only; the exact daily-proration formula (×12/365, fixed divisor regardless of month length or leap year) is sourced from the Excel workbook, since the tariff book doesn't specify one | `Tariff.CalculateDailyFixedCharge`, `TariffGoldenDataTests.CalculateDailyFixedCharge_MatchesMepdclReferenceWorkbook` |
| Prepaid rebate | 2% of gross EC | 2% of energy charge (tariff §22.1) | 2% of gross EC only (not fixed charge, duty, FPPAS, etc.) | No conflict — Excel confirms rebate is computed per-billing-period on that period's own gross EC, not deferred/aggregated | `TariffGoldenDataTests` |
| Electricity duty — Domestic/BPL | ₹0.05/unit flat | ₹0.05/unit flat (§21) | ₹0.05/unit flat | No conflict | `ElectricityDutyTests` |
| Electricity duty — Industrial | not shown in workbook | ₹0.05/first 15,000, ₹0.045/next 25,000, ₹0.03/remainder (§21) | as tariff book | Workbook doesn't cover Industrial; tariff book is the only source | `ElectricityDutyTests` |
| Electricity duty — Others | not shown in workbook | ₹0.06/unit flat (§21) | ₹0.06/unit flat | No conflict; not independently verified against Excel | `ElectricityDutyTests` |
| **DHT energy charge** | **₹5.87/kVAh** (`DHT PREPAID` sheet) | **₹5.85/kVAh** (§A.2) | **₹5.85/kVAh** | **Documented conflict.** Tariff book governs production. Excel value kept as an explicit, separately named regression fixture. | `DhtTariffDiscrepancyTests.CurrentTariffBookProductionValue_Is5_85PerKvah` (production) and `.LegacyExcelReferenceValue_Is5_87PerKvah_NotUsedInProduction` (reference only) |
| DHT electricity duty category mapping | ₹0.05/unit used for a Domestic HT example (100 units → ₹5) | Duty table lists "Domestic & BPL" / "Industrial" / "Others" — does not explicitly list "Domestic HT" | Domestic HT billed at the Domestic & BPL rate (₹0.05) | **Assumption, not a hard numeric conflict**: the tariff book's duty categories are about consumption category, not voltage level, so "Domestic HT" is read as still "Domestic" for duty purposes. If this reading is wrong, only the category→duty mapping needs to change — documented explicitly in `ElectricityDuty`'s XML doc comment so it's easy to find and correct. | `ElectricityDutyTests.Calculate_DomesticAndBpl_FlatFivePaisaPerUnit` (the `100 -> 5.00` case) |
| BPL/Kutir Jyoti slabs | 4-tier cumulative table: 0–30@4.57, 30–100@5.00, 100–200@5.04, 200+@5.10 (`BPL_Prepaid Calculation` sheet) | "up to 30 kWh: ₹4.57/kWh; excess billed at appropriate normal domestic slab" (§A.1 note) | 4-tier cumulative slab table, same absolute boundaries as DLT | No conflict once read correctly: the tariff book's plain-English rule and the Excel's literal 4-slab table are mathematically the same thing, as long as the 100/200 boundaries stay absolute (not restarted after the 30 kWh tier). No new domain code was needed — the existing cumulative `Tariff`/`TariffSlab` model already handles it. | `BplTariffTests` |
| Cumulative/daily billing method | Each day's EC = `EnergyCharge(currentCumulative) − EnergyCharge(previousCumulative)` (`DLT` sheet, day-by-day rows) | Not stated explicitly (tariff book gives monthly slabs; doesn't say how to bill daily against them) | Cumulative-differencing, as in Excel | This is the only sane way to bill monthly cumulative slabs day-by-day from a smart meter; confirmed against 4+ real rows spanning slab boundaries (crossing into slab 2, crossing into slab 3, and a zero-consumption day) | `Tariff.CalculateEnergyChargeForPeriod`, `TariffGoldenDataTests` |
| Fixed charge on zero-consumption days | Still accrues (e.g. 2026-08-25 through 08-31: consumption 0, fixed charge still 2.958904…) | "minimum charges shall not be billed during disconnection" (§3, about *minimum* charges specifically, not fixed charge) — implies fixed charge otherwise continues | Fixed charge accrues regardless of daily consumption | No conflict; confirms the domain method should be called independently of whether that day had any consumption | `TariffGoldenDataTests.NetBill_MatchesMepdclReferenceWorkbook` (222→222 case) |
| **FPPAS** — amount | `EnergyCharge × Rate` for the prior month (e.g. ₹6,000 × -14% = -₹840; ₹3,250 × +6.65% = ₹216.125) | "billed to the consumers on a monthly basis" (tariff §A.4) — mechanism not specified | as Excel | Tariff book confirms FPPAS is monthly and separate from energy charge but doesn't specify the notification-lag/proration mechanism; that mechanism is sourced entirely from the workbook | `FppasChargeTests.TotalAmount_*` |
| **FPPAS** — timing/proration | Deferred one month, then spread evenly across every day of the *next* billing month (₹840 ÷ 30 June days = ₹28.00/day exactly; ₹216.125 ÷ 31 Oct days = ₹6.971774193548387…/day, unrounded) | not specified | as Excel | Same as above — workbook-only mechanism, confirmed against both a negative and a positive worked example | `FppasChargeTests.AllocateAcrossDays_*` |
| **TMC** (Transformer Maintenance Charge) | not shown in either workbook (column exists in `Individual Charge Calculation` header, but every example row is 0) | ₹20/kVA/month at 11 kV or 33 kV, ₹25/kVA/month at 132 kV (§5.3); opt-in only (§5.4); basis is installed capacity for exclusive use or contracted demand/connected load for shared use (§5.1–5.2) | as tariff book | Tariff book is the only source — no Excel example to cross-check. Choosing the correct basis (installed capacity vs. contracted demand/load) for a given consumer is left to the caller, since that depends on transformer ownership/usage facts this calculator has no way to know. | `TransformerMaintenanceChargeTests` |
| **CPMC** (CT-PT Set Maintenance Charge) | not shown in either workbook (same as TMC) | ₹800 (11kV 3-wire) / ₹1,000 (11kV 4-wire) / ₹1,500 (33kV 3-wire) / ₹1,900 (33kV 4-wire) per month (§4.1); opt-in only (§4.2); no 132 kV rate defined | as tariff book | Tariff book is the only source. MePDCL's reference workbook notes CPMC "shall be levied monthly only for HT consumers whose metering is done on the LT side" — that eligibility check is left to the caller, not this calculator, which only prices an opted-in, eligible request. | `CtPtMaintenanceChargeTests` |

## Explicitly NOT implemented / NOT verified this pass

These are real, documented gaps — not silently dropped:

- **Non-communicating meter estimation/proration algorithm.** The `Prepaid Calculation Category
  wise.xlsx` workbook (both `DLT` and `DLT exception` sheets) documents a 5-step algorithm:
  total consumption over the gap ÷ number of non-comm days = average daily consumption, then
  that average is split across calendar days **respecting month boundaries**, and each month's
  portion is billed at that month's own tariff/slab position. The worked example in the
  workbook uses illustrative July/August numbers that don't line up with the specific
  Aug–Sep date rows shown alongside it, so the exact day-count/boundary rules need
  confirmation from MePDCL before implementing — the algorithm shape is real, the precise
  edge-case arithmetic isn't fully unambiguous from this workbook alone.
- ~~**FPPAS.**~~ Implemented and wired into `PrepaidBill` (see `FppasCharge`, `PrepaidBill.FppasAmount`/`FppasChargeId`,
  and the API's per-bill breakdown at `GET /api/v1/consumers/{accountNumber}`) — verified live
  against real PostgreSQL. **Still missing**: automatic scheduling of *when* a newly notified
  FPPAS rate gets picked up and applied to the next billing cycle — today a caller must
  construct the `FppasCharge` and pass its daily share into `PrepaidBill` explicitly (see
  `DbSeeder` for the pattern). That scheduling/orchestration is the remaining follow-up.
- ~~**TMC / CPMC.**~~ Implemented and wired into `PrepaidBill` (`TmcAmount`/`CpmcAmount`,
  included in `Amount`) — verified live against real PostgreSQL (existing residential DLT
  demo bill correctly still shows both as zero, since that consumer owns no transformer/CT-PT
  set). No example values exist in either workbook to regression-test a *non-zero* wired-in
  case against, so that's covered by hand-verified unit tests instead
  (`PrepaidBillTests.Amount_ComposesAllSixOptionalAndCoreComponentsTogether`). **Still
  missing**: per-consumer equipment facts (does this consumer own a transformer/CT-PT set,
  have they opted into MePDCL maintenance, is transformer usage exclusive or shared) are not
  modeled on `Consumer` — today a caller must compute `TmcAmount`/`CpmcAmount` via the
  calculators themselves and pass them into `PrepaidBill` explicitly, the same pattern as
  FPPAS's daily share.
- **Arrear recovery.** Column exists in the `Individual Charge Calculation` sheet but every
  example row has it at 0 — no worked example to regression-test against.
- **TOU (time-of-use) tariffs** for Industrial HT/EHT — present in the tariff book, not present
  in either workbook, not implemented.

## Assumptions made (flagged, not silently decided)

1. Industrial electricity-duty slab thresholds (15,000 / 25,000 units) are assumed to be
   **per billing month** — the tariff book doesn't state the period explicitly, and neither
   workbook has an Industrial example to confirm against.
2. "Domestic HT" is billed at the Domestic & BPL duty rate (see table row above).

Both are called out explicitly in `ElectricityDuty`'s doc comments in the code, not buried only
here, so a future change is easy to locate.
