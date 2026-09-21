namespace PrepaidEngine.Domain;

/// <summary>
/// Fixed figures from the MePDCL Electricity Distribution Tariff for FY 2026-27 (effective 1 April 2026; MSERC Order on Case No. 11 of 2025) that
/// are not per-schedule tariff data: statutory and miscellaneous charges, prepaid meter facilities, and the rules the daily prepaid bill relies
/// on. Kept in one place so billing, the Calculation Workbench and the Tariff &amp; Parameters screen all quote the same numbers. Per-schedule rates
/// (slabs, fixed charge, rebate, emergency credit, vend limits, initial credit) are tariff data, not here.
/// </summary>
public static class TariffBookParameters
{
    // ---- Prepaid facilities (§22)
    public const decimal PrepaidEnergyRebatePercent = 2.00m;
    public const string CreditHours = "16:00 to 11:00 the next day, and on official holidays";
    public const decimal EmergencyCreditGeneralPurpose = 2000m;
    public const decimal EmergencyCreditOthers = 200m;
    public const decimal MinimumVendAmount = 500m;

    // ---- Billing rules
    /// <summary>1 HP = 0.746 kW (MSERC Supply Code 2026, clause 1.6 (x)); Agriculture's fixed charge is per kW or per HP.</summary>
    public const decimal KwPerHp = 0.746m;

    /// <summary>Surcharge on energy charges where supply is at a voltage lower than classified, or an HT consumer is metered on the LT side (Supply Code 2.3.1).</summary>
    public const decimal LtSideMeteringSurchargeRate = 0.03m;

    /// <summary>HT fixed charge is never billed below 50 kW or 56 kVA of demand (§3.2).</summary>
    public const decimal HtMinimumChargeableKw = 50m;
    public const decimal HtMinimumChargeableKva = 56m;

    // ---- Reconnection and delayed payment (§12-§14). Postpaid concepts, listed for reference: a prepaid meter that runs out of credit is not a
    // disconnection (§13.8), so these are not charged for that.
    public const decimal ReconnectionChargeSinglePhaseLt = 550m;
    public const decimal ReconnectionChargeThreePhaseLtBelow50Kw = 1100m;
    public const decimal ReconnectionChargeHt = 2200m;
    public const decimal DisconnectReconnectFeeOtherThanNonPayment = 150m;
    public const decimal DelayedPaymentChargeRatePer30Days = 0.01m;
    public const decimal DisconnectionInterestRatePerYear = 0.12m;
    public const int BillDueDays = 15;

    public static decimal HpToKw(decimal hp) => hp * KwPerHp;
}
