using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain;

/// <summary>Everything the daily prepaid bill for one consumer and one day is worked out from.</summary>
/// <param name="Tariff">The consumer's assigned tariff.</param>
/// <param name="ConnectedLoadOrContractDemand">kW for LT, kVA for kVA-based schedules; for Agriculture billed per HP, convert first with <see cref="TariffBookParameters.HpToKw"/>.</param>
/// <param name="DayKwh">The day's consumption from the daily load profile.</param>
/// <param name="MonthToDateKwhBefore">Consumption already billed earlier in the same calendar month, so slabs and tiered duty continue rather than restart each day.</param>
/// <param name="MeteredOnLtSide">HT consumer metered on the LT side of the transformer: adds the 3% surcharge.</param>
/// <param name="TmcMonthly">Monthly Transformer Maintenance Charge if the consumer opted for it (else 0).</param>
/// <param name="CpmcMonthly">Monthly CT-PT Set Maintenance Charge if opted for (else 0).</param>
/// <param name="FppasDailyShare">This day's share of a notified FPPAS (may be negative).</param>
/// <param name="TouKvahByBand">Consumption by Time-of-Day band, for ToD tariffs, when band data is available.</param>
public sealed record DailyBillInput(
    Tariff Tariff,
    decimal ConnectedLoadOrContractDemand,
    decimal DayKwh,
    decimal MonthToDateKwhBefore = 0m,
    bool MeteredOnLtSide = false,
    decimal TmcMonthly = 0m,
    decimal CpmcMonthly = 0m,
    decimal FppasDailyShare = 0m,
    IReadOnlyDictionary<string, decimal>? TouKvahByBand = null);

/// <summary>The daily bill, component by component. <see cref="Total"/> is the exact sum; <see cref="TotalRounded"/> is what is debited.</summary>
public sealed record DailyBillBreakdown(
    decimal DayKwh,
    decimal MonthToDateKwhBefore,
    decimal GrossEnergyCharge,
    decimal PrepaidRebate,
    decimal NetEnergyCharge,
    decimal FixedCharge,
    decimal ElectricityDuty,
    decimal LtSideMeteringSurcharge,
    decimal Tmc,
    decimal Cpmc,
    decimal FppasShare,
    decimal Total,
    decimal TotalRounded,
    IReadOnlyList<string> Notes);

/// <summary>
/// How one day's prepaid bill is built, in one place: the daily billing run and the Calculation Workbench both call this, so what the
/// workbench previews is what the run debits.
///
/// <c>Total = (energy charge − 2% prepaid rebate) + daily fixed charge + electricity duty + LT-side metering surcharge + TMC + CPMC + FPPAS share</c>.
/// <list type="bullet">
/// <item><description>Energy charge: the difference between the slab charge at the month-to-date consumption including today and before today,
/// so the slabs run across the calendar month (a day that crosses 100 kWh is priced part at the first slab, part at the next). Time-of-Day-only
/// tariffs (IHT, IEHT) price by band when band data is given.</description></item>
/// <item><description>Rebate: 2% (per tariff) of the gross energy charge only.</description></item>
/// <item><description>Fixed charge: monthly rate × demand × 12 / 365 every day, including days with no consumption; demand is not taken below the tariff's
/// minimum chargeable demand (HT: 56 kVA).</description></item>
/// <item><description>Duty: the statutory per-unit duty, tiered cumulatively for Industrial.</description></item>
/// <item><description>TMC and CPMC are monthly charges, prorated ×12/365 like the fixed charge.</description></item>
/// </list>
/// Things it cannot know are said in <see cref="DailyBillBreakdown.Notes"/> rather than guessed silently: kVAh tariffs are billed on kWh (the
/// daily profile has no kVAh) and ToD tariffs without band data are priced at the Normal rate.
/// </summary>
public static class DailyBillCalculator
{
    public static DailyBillBreakdown Calculate(DailyBillInput input)
    {
        var tariff = input.Tariff;
        if (input.DayKwh < 0) throw new ArgumentOutOfRangeException(nameof(input), "Consumption cannot be negative.");
        if (input.MonthToDateKwhBefore < 0) throw new ArgumentOutOfRangeException(nameof(input), "Month-to-date consumption cannot be negative.");
        if (input.ConnectedLoadOrContractDemand < 0) throw new ArgumentOutOfRangeException(nameof(input), "Connected load / contract demand cannot be negative.");

        var notes = new List<string>();
        if (tariff.EnergyUnit == EnergyUnit.Kvah)
            notes.Add("This schedule is quoted per kVAh. The daily load profile carries kWh only, so kVAh is taken as equal to kWh.");

        decimal gross;
        if (tariff.Slabs.Count > 0)
        {
            gross = tariff.CalculateEnergyChargeForPeriod(input.MonthToDateKwhBefore, input.MonthToDateKwhBefore + input.DayKwh);
        }
        else if (input.TouKvahByBand is { Count: > 0 })
        {
            gross = tariff.CalculateTouEnergyCharge(input.TouKvahByBand);
        }
        else
        {
            var normal = tariff.TouPeriods.FirstOrDefault(p => string.Equals(p.Label, "Normal", StringComparison.OrdinalIgnoreCase)) ?? tariff.TouPeriods.First();
            gross = input.DayKwh * normal.RatePerKvah;
            notes.Add($"This is a Time-of-Day tariff and the day has no per-band consumption, so all of it is priced at the {normal.Label} rate (₹{normal.RatePerKvah:0.00}).");
        }

        var rebate = gross * (tariff.PrepaidEnergyRebatePercent / 100m);
        var net = gross - rebate;
        var fixedCharge = tariff.CalculateDailyFixedCharge(input.ConnectedLoadOrContractDemand);
        var duty = ElectricityDuty.CalculateForPeriod(tariff.Category, input.MonthToDateKwhBefore, input.MonthToDateKwhBefore + input.DayKwh);
        var surcharge = input.MeteredOnLtSide ? gross * TariffBookParameters.LtSideMeteringSurchargeRate : 0m;
        var tmc = input.TmcMonthly * 12m / Tariff.DaysPerYearForFixedChargeProration;
        var cpmc = input.CpmcMonthly * 12m / Tariff.DaysPerYearForFixedChargeProration;

        var total = net + fixedCharge + duty + surcharge + tmc + cpmc + input.FppasDailyShare;
        return new DailyBillBreakdown(
            input.DayKwh, input.MonthToDateKwhBefore, gross, rebate, net, fixedCharge, duty, surcharge, tmc, cpmc, input.FppasDailyShare,
            total, Math.Round(total, 2, MidpointRounding.AwayFromZero), notes);
    }
}
