# Tariff book FY 2026-27: comparison with the engine

Source: *MePDCL Electricity Distribution Tariff and Miscellaneous Charges, effective 1 April 2026* (notification dated 12 June 2026, MSERC Order on
Case No. 11 of 2025, MSERC Supply Code 1 of 2026). Every table and clause was read and compared with the domain code, the data, the Tariff &amp;
Parameters screen, the Calculation Workbench and the daily billing run before anything was changed. This page records what matched, what did not,
and what was done. Where the book leaves something open, the assumption is stated rather than hidden.

Status key: **Same** already correct, nothing changed · **Fixed** was wrong or missing, now done · **Recorded** kept as data or a reference, not
computed · **Open** needs a decision or data the system does not have.

## 1. Rates and charges

| Book item | Book value | Before | Now | Status |
|---|---|---|---|---|
| DLT energy slabs | 100 @ 5.00, next 100 @ 5.04, above 200 @ 5.10 | Same (one tariff seeded) | Same | Same |
| DLT fixed charge | ₹90 /kW /month | Same | Same | Same |
| CLT, GP, WSLT, PL, EVLT, CRM, AP, ILT | 7.45, 7.35, 7.00, 7.00, 6.00, 4.75, 3.15, 6.80; fixed 170, 180, 180, 180, none, 55, 130, 170 | **Not in the system**: only DLT existed | All present as active tariffs | Fixed |
| Public Lighting (metered) | ₹7.00, fixed ₹180 /kW | No such category | New category `PublicLighting`, schedule PL | Fixed |
| DHT, CHT, BS, WSHT, IHT, FAHT, EVHT | 5.85, 6.00, 6.10, 6.60, 5.55, 5.55, 6.00 /kWh for EVHT; fixed 350, 390, 420, 410, 340, 500, none | Not in the system | All present | Fixed |
| IEHT, FAEHT | 6.60, 5.60; fixed 500, 500 | Not in the system | All present | Fixed |
| IHT Time-of-Day | 5.55 normal 06-17, 6.66 peak 17-23, 4.72 off-peak 23-06 | Model supported it, no data | Loaded | Fixed |
| IEHT Time-of-Day | 6.60 / 7.92 / 5.61 | Same | Loaded | Fixed |
| Kutir Jyoti / BPL metered | ₹4.57 up to 30 kWh, then the domestic slabs | Modelled as four slabs, none seeded | KJM loaded: 30 @ 4.57, then 5.00 / 5.04 / 5.10 (domestic boundaries kept). The book names no fixed charge, so none | Fixed |
| Kutir Jyoti unmetered (KJU) | ₹210 per connection per month | n/a | Not modelled: an unmetered connection has no prepaid meter | Open (out of scope) |
| Energy unit | kWh for most LT, kVAh for HT, EHT and ILT | Not recorded on a tariff | New `EnergyUnit` on every tariff | Fixed |
| Fixed charge basis | per kW, per kVA, per kW or HP (Agriculture), none for EV | Not recorded | New `FixedChargeBasis`; 1 HP = 0.746 kW available in the workbench | Fixed |
| HT minimum demand | Fixed charge is never on less than 50 kW or 56 kVA (§3.2) | Not enforced | `MinimumChargeableDemand` (56 kVA) on HT kVA schedules, applied in the fixed charge | Fixed |
| Minimum charges (§3) | The fixed charge is the monthly minimum | Same idea | Same | Same |
| Electricity duty (§21) | Domestic and BPL 5 paisa; Industrial 5 paisa first 15,000, 4.5 next 25,000, 3 after; others 6 paisa | Correct in `ElectricityDuty` | Same. Not used by the daily run (see 3) | Same |
| Prepaid rebate (§22.1) | 2% on energy charge | Correct in tariffs | Same | Same |
| FPPAS (§A.4) | Monthly, notified | Domain object exists, never applied by the run | Same as before. The workbench can preview a share | Open |
| TMC, CPMC (§4, §5) | ₹20 /25 per kVA per month; ₹800 / 1,000 / 1,500 / 1,900 per month | Correct constants | Same; now also in the daily bill calculation | Same |
| LT-side metering surcharge | 3% of energy charges where an HT consumer is metered on the LT side (Supply Code 2.3.1) | Not modelled | In the daily calculation and the workbench | Fixed |

## 2. Prepaid meter facilities (§22)

| Book item | Book value | Before | Now | Status |
|---|---|---|---|---|
| Emergency credit | ₹2,000 General Purpose, ₹200 others | Per tariff, only DLT (₹200) | Set per tariff in the catalogue (GP and BS ₹2,000) | Fixed |
| Vend limits | Max ₹15,000 single phase, ₹25,000 three phase; General Purpose ₹50,000 / ₹1,00,000 | Per tariff, only DLT | Set per tariff | Fixed |
| Minimum vend | ₹500 printed once beside the General Purpose row | ₹500 on DLT | ₹500 on General Purpose only; every other schedule has no minimum. The recharge endpoint no longer applies a blanket ₹500: it enforces the consumer's own tariff's minimum and maximum (the tariff limits were previously not enforced at all) | Fixed (decided: as the book) |
| Initial credit | ₹100 / ₹200 (single / three phase); General Purpose ₹1,000 / ₹5,000, adjusted in the first vend | Not stored | Stored on each tariff and shown. **Not applied to a wallet**: nothing installs a meter or vends first in this system yet | Recorded |
| No load security deposit | §22.2, §1.3.5 | Not charged | Same | Same |
| Credit hours | "From 4:00 PM to 11:00 AM and official holidays" | The RC/DC screen and API allow a manual disconnect between **9:00 AM and 2:00 PM** ("Happy Hours", from the AMISP integration document) | **Disconnection is allowed only between 11:00 AM and 4:00 PM IST.** The 9 AM to 2 PM window is gone. Manual disconnects outside it are refused. The automatic disconnect when the balance passes the emergency credit is held until the window opens: a background worker (every 5 minutes) disconnects then those still over the limit. Reconnection is never held back. Official holidays are not modelled (no holiday calendar) and are stated as such | Fixed (decided: as the book) |
| Running out of credit | Not a disconnection (§13.8) | No reconnection charge is levied | Same, and stated on the parameters screen | Same |

## 3. How the daily prepaid bill is generated

This is where the engine differed most from the book.

| Part of the daily bill | Before | Now |
|---|---|---|
| Energy charge | The slabs were applied to **each day's consumption on its own**, so every day restarted at the first slab and a consumer who used 300 kWh in a month was priced almost entirely at ₹5.00 | Slab charge on the month-to-date consumption including today, less the same for the days before: a day that crosses 100 kWh is priced part at each slab. The month-to-date is read once per batch from the days already billed in the same calendar month |
| Prepaid rebate | 2% of energy charge | Same |
| Fixed charge | Monthly × load × 12 / 365, every day | Same, and never on less than the minimum chargeable demand (HT 56 kVA) |
| Electricity duty | **Left out of the debit** | Added each day; Industrial tiers run across the month |
| LT-side metering surcharge, TMC, CPMC, FPPAS share | Left out | In the calculation. The daily run passes zero for them because the system does not yet record per consumer whether they apply (metering side, opted-in maintenance, a notified FPPAS rate). The Calculation Workbench previews them |
| Record of the bill | Only a wallet debit for one number | A `DailyBill` row per day with every component, the month-to-date it started from, the tariff, and any assumption made |
| Time-of-Day (IHT, IEHT) | Priced as zero energy (no slabs) | Split into bands from the load survey intervals MDM sends every 15 or 30 minutes (each interval goes to the band its IST start time falls in). Energy the intervals do not cover, or a day with no intervals, is priced at the Normal rate and the bill says so |
| kVAh schedules (HT, EHT, ILT) | Billed on kWh, silently | Billed on the daily load profile's kVAh. Electricity duty stays on kWh units. A day whose profile has no kVAh falls back to kWh and the bill says so. The bill records the energy and unit it was worked on |

The daily run and the Calculation Workbench call the same `DailyBillCalculator`, so what the workbench shows is what a real day is debited.

## 4. Modules

| Area | Finding | Change |
|---|---|---|
| Tariff &amp; Parameters | Listed whatever tariffs existed. Unrelated to the book. Showed no schedule code, voltage, energy unit or fixed-charge basis | Schedule code and voltage on every row, energy rate and fixed charge as the book quotes them, filter by voltage, search, a **book check** chip per tariff (Matches / Differs with the fields), a Tariff Book and Parameters tab (book comparison, prepaid facilities, duty, maintenance, reconnection, surcharge), and classification on the detail page |
| Category list | The frontend had four categories numbered differently from the backend (frontend 1 = BPL, backend 1 = Non-Domestic; frontend 2 = Industrial, backend 2 = General Purpose), so a change request drafted as "Industrial" was saved as General Purpose. The form offered only four categories | Frontend now mirrors the backend's eleven categories; the change form offers all of them |
| Calculation Workbench | Simulated a whole month from one consumption figure and refused Time-of-Day tariffs | Simulates one day's bill like the run: month-to-date, load in kW / kVA / HP, optional charges, Time-of-Day bands, notes on assumptions, a breakdown bar, example days |
| Revisions | A revision (change request) created a new tariff without the new classification fields | A revision keeps the schedule code, voltage, energy unit, basis, minimum demand and initial credit of the tariff it replaces |

## 5. Things found that need your decision

Decided, and done: credit hours (disconnection 11 AM to 4 PM only), the development DLT tariff (the demo seed now replaces any seeded tariff that
differs from the book with the book version and moves its consumers, so the book check reads 19 of 19; a real deployment uses the change workflow),
minimum recharge (General Purpose only), kVAh and Time-of-Day billing (from the daily load profile's kVAh and the load survey intervals). Also
fixed on the way: activating a tariff revision now moves the tariff's consumers to the new version (they used to stay on the retired one, so a
revision never reached billing).

Still open:

1. **Per-consumer facts** (LT-side metering, opted-in TMC and CPMC, a notified FPPAS rate) need somewhere to be recorded before the daily run can
   apply them.

## 6. Not applicable to prepaid daily billing (kept as reference)

Application, estimate and supervision charges; security deposits for postpaid; delayed payment charge (1% per 30 days) and interest after
disconnection (12% a year); reconnection charges (₹550 / ₹1,100 / ₹2,200; ₹150 for other reasons); compensation for malpractice; meter tests;
temporary supply; change of name. The amounts are listed on the Tariff and Parameters screen for reference and are not charged by the daily run.
