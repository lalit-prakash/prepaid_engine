using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A slab-based tariff plan, filed under a regulatory <see cref="ConsumerCategory"/>, used to
/// compute bill amounts from metered consumption and connected load/contract demand.
///
/// Rates, fixed charges, rebates, emergency-credit limits and vend limits are all data on this
/// entity rather than hard-coded: they come from the utility's published tariff order (e.g. a
/// MePDCL/MSERC tariff notification) and differ per category and per revision.
/// </summary>
public class Tariff
{
    /// <summary>
    /// Divisor used to prorate a monthly fixed/demand charge into a daily amount for prepaid
    /// daily billing: <c>DailyFixedCharge = MonthlyRate × Load × 12 / 365</c>. Verified against
    /// MePDCL's own reference calculation workbook ("Prepaid bill calculation.xlsx" /
    /// "Prepaid Calculation Category wise.xlsx"): the daily fixed charge for a 1&#160;kW DLT
    /// consumer at ₹90/kW/month is consistently ₹2.958904109589041 = 90 × 12 / 365, on every
    /// day of the billing cycle including zero-consumption days, and the divisor stays 365
    /// regardless of the actual number of days in that particular month or a leap year — this
    /// is a deliberate simplification in the source workbook, not an inference on our part.
    /// </summary>
    public const decimal DaysPerYearForFixedChargeProration = 365m;

    private readonly List<TariffSlab> _slabs = new();
    private readonly List<TouPeriod> _touPeriods = new();

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public ConsumerCategory Category { get; private set; }

    /// <summary>Active until a later <see cref="TariffChangeRequest"/> supersedes it and reaches
    /// its commencement date — see <see cref="TariffLifecycleStatus"/>'s doc comment for why this
    /// never requires editing any past bill's tariff reference.</summary>
    public TariffLifecycleStatus Status { get; private set; } = TariffLifecycleStatus.Active;

    public IReadOnlyCollection<TariffSlab> Slabs => _slabs.AsReadOnly();

    /// <summary>
    /// Time-of-Day rate bands for this tariff (e.g. Normal/Peak/Off-peak), if any. Only the
    /// tariff book's Industrial HT (IHT) and Industrial EHT (IEHT) categories define these;
    /// empty for every other tariff. See <see cref="CalculateTouEnergyCharge"/>.
    /// </summary>
    public IReadOnlyCollection<TouPeriod> TouPeriods => _touPeriods.AsReadOnly();

    /// <summary>Fixed/demand charge per unit of connected load or contract demand (₹/kW or ₹/kVA per month).</summary>
    public decimal FixedChargePerUnitPerMonth { get; private set; }

    /// <summary>
    /// Percentage discount on the energy charge granted to prepaid consumers under this
    /// tariff (e.g. MePDCL's 2% prepaid rebate). Zero for tariffs with no such rebate.
    /// </summary>
    public decimal PrepaidEnergyRebatePercent { get; private set; }

    /// <summary>
    /// Emergency credit limit (₹) a prepaid consumer under this tariff may draw beyond a
    /// zero balance before being disconnect-eligible. This is a distinct, separately tracked
    /// allowance — see <see cref="PrepaidWallet.EmergencyCreditLimit"/> — not part of the
    /// normal wallet balance.
    /// </summary>
    public decimal EmergencyCreditLimit { get; private set; }

    /// <summary>Minimum/maximum recharge (vend) amount permitted per transaction, by meter phase; null if unconstrained.</summary>
    public decimal? MinVendAmountSinglePhase { get; private set; }
    public decimal? MaxVendAmountSinglePhase { get; private set; }
    public decimal? MinVendAmountThreePhase { get; private set; }
    public decimal? MaxVendAmountThreePhase { get; private set; }

    public Tariff(
        Guid id,
        string name,
        ConsumerCategory category,
        IEnumerable<TariffSlab> slabs,
        decimal fixedChargePerUnitPerMonth = 0m,
        decimal prepaidEnergyRebatePercent = 0m,
        decimal emergencyCreditLimit = 0m,
        decimal? minVendAmountSinglePhase = null,
        decimal? maxVendAmountSinglePhase = null,
        decimal? minVendAmountThreePhase = null,
        decimal? maxVendAmountThreePhase = null,
        IEnumerable<TouPeriod>? touPeriods = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (fixedChargePerUnitPerMonth < 0)
            throw new ArgumentOutOfRangeException(nameof(fixedChargePerUnitPerMonth));
        if (prepaidEnergyRebatePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(prepaidEnergyRebatePercent), "Rebate must be between 0 and 100.");
        if (emergencyCreditLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(emergencyCreditLimit));
        ValidateVendRange(minVendAmountSinglePhase, maxVendAmountSinglePhase, nameof(minVendAmountSinglePhase));
        ValidateVendRange(minVendAmountThreePhase, maxVendAmountThreePhase, nameof(minVendAmountThreePhase));

        Id = id;
        Name = name;
        Category = category;
        FixedChargePerUnitPerMonth = fixedChargePerUnitPerMonth;
        PrepaidEnergyRebatePercent = prepaidEnergyRebatePercent;
        EmergencyCreditLimit = emergencyCreditLimit;
        MinVendAmountSinglePhase = minVendAmountSinglePhase;
        MaxVendAmountSinglePhase = maxVendAmountSinglePhase;
        MinVendAmountThreePhase = minVendAmountThreePhase;
        MaxVendAmountThreePhase = maxVendAmountThreePhase;

        _slabs.AddRange(slabs ?? throw new ArgumentNullException(nameof(slabs)));
        if (touPeriods is not null)
            _touPeriods.AddRange(touPeriods);

        if (_slabs.Count == 0 && _touPeriods.Count == 0)
            throw new ArgumentException("A tariff must define at least one slab or ToD period.", nameof(slabs));
    }

    // EF Core / serialization
    private Tariff()
    {
        Name = string.Empty;
    }

    private static void ValidateVendRange(decimal? min, decimal? max, string paramName)
    {
        if (min is < 0 || max is < 0)
            throw new ArgumentOutOfRangeException(paramName, "Vend amounts cannot be negative.");
        if (min.HasValue && max.HasValue && min.Value > max.Value)
            throw new ArgumentException("Minimum vend amount cannot exceed the maximum.", paramName);
    }

    /// <summary>
    /// Computes the energy charge for the given consumption by summing each slab's contribution.
    /// Does not include fixed charges, rebates, duty, or surcharge.
    /// </summary>
    public decimal CalculateEnergyCharge(decimal consumptionKwh)
    {
        if (consumptionKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(consumptionKwh));

        return _slabs.Sum(slab => slab.KwhWithinSlab(consumptionKwh) * slab.RatePerKwh);
    }

    /// <summary>
    /// Computes the monthly fixed/demand charge for the given connected load or contract demand.
    /// </summary>
    public decimal CalculateFixedCharge(decimal connectedLoadOrContractDemand)
    {
        if (connectedLoadOrContractDemand < 0)
            throw new ArgumentOutOfRangeException(nameof(connectedLoadOrContractDemand));

        return FixedChargePerUnitPerMonth * connectedLoadOrContractDemand;
    }

    /// <summary>
    /// Computes the daily-prorated fixed/demand charge for prepaid daily billing:
    /// <c>MonthlyRate × Load × 12 / 365</c> (see <see cref="DaysPerYearForFixedChargeProration"/>).
    /// This accrues every day of the billing cycle, including days with zero consumption —
    /// callers should not skip calling this just because a day's energy charge is zero.
    /// </summary>
    public decimal CalculateDailyFixedCharge(decimal connectedLoadOrContractDemand)
    {
        return CalculateFixedCharge(connectedLoadOrContractDemand) * 12m / DaysPerYearForFixedChargeProration;
    }

    /// <summary>
    /// Computes the gross energy charge for one billing period (typically one day of prepaid
    /// billing) from a smart meter's cumulative reading, as the difference between the slab
    /// charge at the new cumulative reading and at the previous one. This is the correct way to
    /// bill against monthly cumulative slabs from a cumulative meter — treating each day's
    /// consumption as if it restarted the slabs from zero would misprice every day after the
    /// consumer crosses a slab boundary. Verified against MePDCL's reference calculation
    /// workbook: e.g. for the DLT slabs (0-100@5.00, 100-200@5.04, 200+@5.10), a day moving the
    /// cumulative reading from 90 to 102 kWh bills ₹60.08 — exactly
    /// CalculateEnergyCharge(102) − CalculateEnergyCharge(90) = 510.08 − 450.00.
    /// </summary>
    public decimal CalculateEnergyChargeForPeriod(decimal previousCumulativeKwh, decimal currentCumulativeKwh)
    {
        if (previousCumulativeKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(previousCumulativeKwh));
        if (currentCumulativeKwh < previousCumulativeKwh)
            throw new ArgumentOutOfRangeException(nameof(currentCumulativeKwh), "Current cumulative reading cannot be lower than the previous one.");

        return CalculateEnergyCharge(currentCumulativeKwh) - CalculateEnergyCharge(previousCumulativeKwh);
    }

    /// <summary>
    /// Computes the net prepaid charge for a billing period: energy charge (after the prepaid
    /// rebate, if any) plus the fixed charge. Excludes electricity duty and other statutory
    /// surcharges, which are applied outside the tariff itself.
    /// </summary>
    public decimal CalculateNetPrepaidCharge(decimal consumptionKwh, decimal connectedLoadOrContractDemand)
    {
        var energyCharge = CalculateEnergyCharge(consumptionKwh);
        var rebate = energyCharge * (PrepaidEnergyRebatePercent / 100m);
        var fixedCharge = CalculateFixedCharge(connectedLoadOrContractDemand);

        return energyCharge - rebate + fixedCharge;
    }

    /// <summary>
    /// Which <see cref="TouPeriod"/> a given time of day falls under, by label (e.g. "Normal",
    /// "Peak", "Off-Peak"). Throws if this tariff has no ToD periods configured, or if the
    /// configured periods leave a gap that doesn't cover <paramref name="timeOfDay"/> — a
    /// correctly configured 24-hour schedule (as the tariff book's IHT/IEHT schedules are)
    /// should never leave a gap.
    /// </summary>
    public string ClassifyTimeOfDay(TimeSpan timeOfDay)
    {
        if (_touPeriods.Count == 0)
            throw new InvalidOperationException($"Tariff '{Name}' has no ToD periods configured.");

        var period = _touPeriods.FirstOrDefault(p => p.Contains(timeOfDay));
        if (period is null)
            throw new InvalidOperationException($"No ToD period on tariff '{Name}' covers {timeOfDay:hh\\:mm} — the configured schedule has a gap.");

        return period.Label;
    }

    /// <summary>
    /// Computes the Time-of-Day energy charge from consumption already broken down by ToD
    /// period label (as MDM/HES interval data aggregated per band would provide) — e.g.
    /// <c>{"Normal": 500, "Peak": 120, "Off-Peak": 80}</c> kVAh. Every key must match a
    /// configured <see cref="TouPeriod"/>'s <see cref="TouPeriod.Label"/> exactly; an unknown
    /// label throws rather than silently being ignored, since that would otherwise
    /// under-bill without any indication why.
    /// </summary>
    public decimal CalculateTouEnergyCharge(IReadOnlyDictionary<string, decimal> consumptionKvahByPeriodLabel)
    {
        if (_touPeriods.Count == 0)
            throw new InvalidOperationException($"Tariff '{Name}' has no ToD periods configured.");

        var knownLabels = _touPeriods.Select(p => p.Label).ToHashSet();
        var unknownLabel = consumptionKvahByPeriodLabel.Keys.FirstOrDefault(label => !knownLabels.Contains(label));
        if (unknownLabel is not null)
            throw new ArgumentException($"'{unknownLabel}' is not a ToD period configured on tariff '{Name}'.", nameof(consumptionKvahByPeriodLabel));

        decimal total = 0m;
        foreach (var period in _touPeriods)
        {
            var consumption = consumptionKvahByPeriodLabel.GetValueOrDefault(period.Label, 0m);
            if (consumption < 0)
                throw new ArgumentOutOfRangeException(nameof(consumptionKvahByPeriodLabel), $"Consumption for '{period.Label}' cannot be negative.");

            total += consumption * period.RatePerKvah;
        }

        return total;
    }

    /// <summary>
    /// Throws if <paramref name="amount"/> falls outside this tariff's configured min/max vend
    /// amount for the given meter phase. A phase with no configured limits is unconstrained.
    /// </summary>
    /// <summary>Marks this tariff superseded — see <see cref="TariffLifecycleStatus"/>. Called
    /// exactly once, when a <see cref="TariffChangeRequest"/> that names this tariff as
    /// <c>SupersedesTariffId</c> is activated at its commencement date.</summary>
    public void Retire()
    {
        if (Status == TariffLifecycleStatus.Retired)
            throw new InvalidOperationException($"Tariff '{Name}' is already retired.");

        Status = TariffLifecycleStatus.Retired;
    }

    public void ValidateVendAmount(decimal amount, MeterPhase phase)
    {
        var (min, max) = phase == MeterPhase.SinglePhase
            ? (MinVendAmountSinglePhase, MaxVendAmountSinglePhase)
            : (MinVendAmountThreePhase, MaxVendAmountThreePhase);

        if (min.HasValue && amount < min.Value)
            throw new ArgumentOutOfRangeException(nameof(amount), $"Recharge amount must be at least {min.Value} for {phase} meters under tariff '{Name}'.");
        if (max.HasValue && amount > max.Value)
            throw new ArgumentOutOfRangeException(nameof(amount), $"Recharge amount cannot exceed {max.Value} for {phase} meters under tariff '{Name}'.");
    }
}
