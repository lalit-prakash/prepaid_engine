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
    private readonly List<TariffSlab> _slabs = new();

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public ConsumerCategory Category { get; private set; }
    public IReadOnlyCollection<TariffSlab> Slabs => _slabs.AsReadOnly();

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
        decimal? maxVendAmountThreePhase = null)
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
        if (_slabs.Count == 0)
            throw new ArgumentException("A tariff must define at least one slab.", nameof(slabs));
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
    /// Throws if <paramref name="amount"/> falls outside this tariff's configured min/max vend
    /// amount for the given meter phase. A phase with no configured limits is unconstrained.
    /// </summary>
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
