using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain;

/// <summary>
/// Electricity duty is a statutory per-unit charge, separate from the tariff's own energy
/// charge/rebate, so it is modeled independently of <see cref="Entities.Tariff"/> rather than
/// as tariff data — the duty schedule is category-based, not tariff-plan-based.
///
/// Rates sourced from the MePDCL Electricity Distribution Tariff (§21, "Electricity Duty &amp;
/// Other Taxes"), effective 1 April 2026, and cross-checked against MePDCL's own reference
/// calculation workbooks:
/// <list type="bullet">
/// <item><description>Domestic &amp; BPL: ₹0.05 per unit (flat).</description></item>
/// <item><description>Industrial: ₹0.05/unit for the first 15,000 units, ₹0.045/unit for the
/// next 25,000, ₹0.03/unit thereafter.</description></item>
/// <item><description>Others: ₹0.06 per unit (flat).</description></item>
/// </list>
///
/// ASSUMPTION (not stated explicitly in the tariff book, needs utility confirmation): the
/// Industrial slab thresholds (15,000 / 25,000 units) are applied per billing month, matching
/// the monthly-cumulative convention used elsewhere in this tariff (energy slabs, fixed
/// charge). The tariff book does not state the slab period explicitly.
///
/// ASSUMPTION: "Domestic HT" (<see cref="ConsumerCategory.Domestic"/> consumers on an HT
/// connection) is billed at the Domestic &amp; BPL duty rate, not the "Others" rate — this
/// matches MePDCL's own DHT reference workbook example (100 units × ₹0.05 = ₹5 duty shown for
/// a DHT consumer), even though the tariff book's duty table does not list "Domestic HT"
/// separately from plain "Domestic". If this reading is wrong, only this mapping needs to
/// change — the per-category rates themselves are the same for every voltage level.
/// </summary>
public static class ElectricityDuty
{
    public const decimal DomesticAndBplRatePerUnit = 0.05m;
    public const decimal OthersRatePerUnit = 0.06m;

    public const decimal IndustrialFirstSlabUnits = 15000m;
    public const decimal IndustrialFirstSlabRatePerUnit = 0.05m;
    public const decimal IndustrialSecondSlabUnits = 25000m;
    public const decimal IndustrialSecondSlabRatePerUnit = 0.045m;
    public const decimal IndustrialRemainingRatePerUnit = 0.03m;

    /// <summary>
    /// Computes electricity duty for the given consumption and category. For
    /// <see cref="ConsumerCategory.Industrial"/>, <paramref name="consumptionKwh"/> is treated
    /// as the consumption already accumulated within the current billing month (see the period
    /// assumption in the type-level documentation) so the tiered slabs apply correctly; callers
    /// billing daily against a cumulative meter should difference two calls the same way
    /// <see cref="Entities.Tariff.CalculateEnergyChargeForPeriod"/> does for the energy charge.
    /// </summary>
    public static decimal Calculate(ConsumerCategory category, decimal consumptionKwh)
    {
        if (consumptionKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(consumptionKwh));

        return category switch
        {
            ConsumerCategory.Domestic or ConsumerCategory.KutirJyotiBpl =>
                consumptionKwh * DomesticAndBplRatePerUnit,

            ConsumerCategory.Industrial => CalculateIndustrial(consumptionKwh),

            _ => consumptionKwh * OthersRatePerUnit
        };
    }

    private static decimal CalculateIndustrial(decimal consumptionKwh)
    {
        var firstSlabUnits = Math.Min(consumptionKwh, IndustrialFirstSlabUnits);
        var remaining = consumptionKwh - firstSlabUnits;

        var secondSlabUnits = Math.Min(remaining, IndustrialSecondSlabUnits);
        remaining -= secondSlabUnits;

        var thirdSlabUnits = remaining;

        return firstSlabUnits * IndustrialFirstSlabRatePerUnit
             + secondSlabUnits * IndustrialSecondSlabRatePerUnit
             + thirdSlabUnits * IndustrialRemainingRatePerUnit;
    }

    /// <summary>
    /// Duty for one billing period from a cumulative meter reading, mirroring
    /// <see cref="Entities.Tariff.CalculateEnergyChargeForPeriod"/> — required for
    /// <see cref="ConsumerCategory.Industrial"/> so the tiered slabs are applied to cumulative
    /// monthly consumption rather than restarting at zero each day; harmless for flat-rate
    /// categories where it's equivalent to duty on the raw daily delta.
    /// </summary>
    public static decimal CalculateForPeriod(ConsumerCategory category, decimal previousCumulativeKwh, decimal currentCumulativeKwh)
    {
        if (previousCumulativeKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(previousCumulativeKwh));
        if (currentCumulativeKwh < previousCumulativeKwh)
            throw new ArgumentOutOfRangeException(nameof(currentCumulativeKwh), "Current cumulative reading cannot be lower than the previous one.");

        return Calculate(category, currentCumulativeKwh) - Calculate(category, previousCumulativeKwh);
    }
}
