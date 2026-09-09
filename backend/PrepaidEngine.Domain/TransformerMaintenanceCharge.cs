using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain;

/// <summary>
/// Transformer Maintenance Charge (TMC) — a fixed monthly charge for MePDCL's maintenance of a
/// consumer-owned transformer, sourced from the MePDCL Electricity Distribution Tariff §5.
///
/// Rates: ₹20/kVA/month at 11 kV or 33 kV; ₹25/kVA/month at 132 kV (§5.3).
///
/// TMC is entirely opt-in (§5.4): if the transformer owner has not opted for MePDCL
/// maintenance, TMC is zero regardless of voltage or capacity — the owner is then responsible
/// for their own transformer's upkeep and replacement.
///
/// The billing basis differs by usage (§5.1–5.2):
/// <list type="bullet">
/// <item><description>Used exclusively by the owner: the transformer's installed capacity.</description></item>
/// <item><description>MePDCL also uses its spare capacity for other consumers: the owner's
/// contracted demand (HT/IEHT) or connected load (LT) instead.</description></item>
/// </list>
/// Choosing the correct basis for a given consumer is the caller's responsibility — that
/// depends on transformer ownership/usage facts this calculator has no way to know.
/// </summary>
public static class TransformerMaintenanceCharge
{
    public const decimal RatePerKvaPerMonth_11kVor33kV = 20.00m;
    public const decimal RatePerKvaPerMonth_132kV = 25.00m;

    /// <summary>TMC when the transformer is used exclusively by its owner — billed on installed capacity.</summary>
    public static decimal CalculateForExclusiveUse(SupplyVoltage voltage, bool optedForMepdclMaintenance, decimal installedTransformerCapacityKva)
    {
        return Calculate(voltage, optedForMepdclMaintenance, installedTransformerCapacityKva);
    }

    /// <summary>
    /// TMC when MePDCL also uses the transformer's spare capacity for other consumers — billed
    /// on the owner's contracted demand (HT/IEHT) or connected load (LT) instead of installed capacity.
    /// </summary>
    public static decimal CalculateForSharedUse(SupplyVoltage voltage, bool optedForMepdclMaintenance, decimal contractedDemandOrConnectedLoadKva)
    {
        return Calculate(voltage, optedForMepdclMaintenance, contractedDemandOrConnectedLoadKva);
    }

    private static decimal Calculate(SupplyVoltage voltage, bool optedForMepdclMaintenance, decimal basisKva)
    {
        if (basisKva < 0)
            throw new ArgumentOutOfRangeException(nameof(basisKva));

        if (!optedForMepdclMaintenance)
            return 0m;

        var rate = voltage == SupplyVoltage.Kv132 ? RatePerKvaPerMonth_132kV : RatePerKvaPerMonth_11kVor33kV;
        return rate * basisKva;
    }
}
