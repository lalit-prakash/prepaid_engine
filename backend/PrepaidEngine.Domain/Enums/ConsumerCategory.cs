namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Regulatory consumption category under which a <see cref="Entities.Tariff"/> is filed
/// (e.g. MSERC's classification for MePDCL). Determines which rate schedule, fixed charge,
/// and prepaid rules (rebate, emergency credit, vend limits) apply.
/// </summary>
public enum ConsumerCategory
{
    Domestic,
    NonDomestic,
    GeneralPurpose,
    PublicWaterSupply,
    Industrial,
    FerroAlloy,
    Agriculture,
    Crematorium,
    ElectricVehicle,
    KutirJyotiBpl,
    /// <summary>Metered public street lighting (tariff schedule PL).</summary>
    PublicLighting
}
