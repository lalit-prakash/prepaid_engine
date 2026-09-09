using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain;

/// <summary>
/// CT-PT Set Maintenance Charge (CPMC) — a fixed monthly charge for MePDCL's maintenance of a
/// consumer-owned CT-PT (Current Transformer - Potential Transformer) metering set, sourced
/// from the MePDCL Electricity Distribution Tariff §4.
///
/// Rates (₹/month), by supply voltage and CT-PT wiring:
/// <list type="bullet">
/// <item><description>11 kV, 3-phase 3-wire: ₹800</description></item>
/// <item><description>11 kV, 3-phase 4-wire: ₹1,000</description></item>
/// <item><description>33 kV, 3-phase 3-wire: ₹1,500</description></item>
/// <item><description>33 kV, 3-phase 4-wire: ₹1,900</description></item>
/// </list>
/// The tariff book does not define a 132 kV CPMC rate.
///
/// CPMC is entirely opt-in (§4.2): if the CT-PT set owner has not opted for MePDCL
/// maintenance, CPMC is zero — the owner is then responsible for the set's own upkeep and
/// replacement. Per MePDCL's own reference workbook note, CPMC applies only to HT consumers
/// whose metering is done on the LT side; that eligibility check is the caller's
/// responsibility, not this calculator's — it only prices an opted-in, eligible request.
/// </summary>
public static class CtPtMaintenanceCharge
{
    private static readonly IReadOnlyDictionary<(SupplyVoltage, CtPtWiring), decimal> Rates =
        new Dictionary<(SupplyVoltage, CtPtWiring), decimal>
        {
            [(SupplyVoltage.Kv11, CtPtWiring.ThreePhaseThreeWire)] = 800.00m,
            [(SupplyVoltage.Kv11, CtPtWiring.ThreePhaseFourWire)] = 1000.00m,
            [(SupplyVoltage.Kv33, CtPtWiring.ThreePhaseThreeWire)] = 1500.00m,
            [(SupplyVoltage.Kv33, CtPtWiring.ThreePhaseFourWire)] = 1900.00m,
        };

    public static decimal Calculate(SupplyVoltage voltage, CtPtWiring wiring, bool optedForMepdclMaintenance)
    {
        if (!optedForMepdclMaintenance)
            return 0m;

        if (!Rates.TryGetValue((voltage, wiring), out var rate))
            throw new ArgumentOutOfRangeException(nameof(voltage), $"No CPMC rate is defined for {voltage}/{wiring} in the tariff book.");

        return rate;
    }
}
