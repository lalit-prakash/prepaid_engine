namespace PrepaidEngine.Infrastructure.MeterData;

/// <summary>Configurable energy-validation tolerances — bound from the "EnergyValidation" section
/// of appsettings.json. Never hard-coded in the validation logic itself (spec: "Do not invent
/// tolerance").</summary>
public class EnergyValidationOptions
{
    public const string SectionName = "EnergyValidation";

    /// <summary>Absolute variance % at/above which a comparison is Warning rather than Pass.</summary>
    public decimal WarningTolerancePct { get; set; } = 2m;

    /// <summary>Absolute variance % at/above which a comparison is Fail rather than Warning.</summary>
    public decimal FailTolerancePct { get; set; } = 5m;
}
