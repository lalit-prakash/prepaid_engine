using System.Globalization;

namespace PrepaidEngine.Api.Settings;

public enum SettingType { Integer, Decimal }

/// <summary>A setting an administrator may change: which configuration key it is, how it is described and what values it accepts.</summary>
public sealed record SettingDefinition(
    string Key, string Group, string Label, string Description, string Unit, SettingType Type, decimal Min, decimal Max, bool AllowBlank = false);

/// <summary>
/// The settings that can be changed from System Settings. Only these keys are read from the database, so a stray row can never override
/// anything else (secrets, connection strings, signing keys). Each is bound to an existing options class, so the value the API uses is
/// the one shown here. A blank value always means "back to the default"; where a setting has no default (AllowBlank), the built-in behaviour applies.
/// </summary>
public static class SettingsRegistry
{
    public static readonly SettingDefinition[] All =
    {
        new("LowBalance:ThresholdRs", "Wallets", "Low balance threshold", "A wallet below this balance counts as low, everywhere: the low-balance SMS and the balance counts. Leave blank to use each wallet's own emergency credit limit (and Rs. 100 for the SMS).", "Rs.", SettingType.Decimal, 0m, 1_000_000m, AllowBlank: true),

        new("EnergyValidation:WarningTolerancePct", "Meter data validation", "Warning tolerance", "A day's energy that differs between meter data sources by more than this raises a warning.", "%", SettingType.Decimal, 0m, 100m),
        new("EnergyValidation:FailTolerancePct", "Meter data validation", "Failure tolerance", "A difference above this fails the validation. Must not be lower than the warning tolerance.", "%", SettingType.Decimal, 0m, 100m),

        new("SlaMonitoring:DlpIngestionTargetMinutes", "Service level targets", "Daily load profile ingestion", "Target time for a daily load profile to be ingested.", "minutes", SettingType.Integer, 1m, 10_080m),
        new("SlaMonitoring:BillingRunTargetMinutes", "Service level targets", "Billing run", "Target time for the daily billing run to finish.", "minutes", SettingType.Integer, 1m, 10_080m),
        new("SlaMonitoring:RechargeCompletionTargetMinutes", "Service level targets", "Recharge completion", "Target time from a recharge starting to it completing.", "minutes", SettingType.Integer, 1m, 10_080m),
        new("SlaMonitoring:MeterCreditTargetMinutes", "Service level targets", "Meter credit", "Target time for a meter credit command to be acknowledged.", "minutes", SettingType.Integer, 1m, 10_080m),
        new("SlaMonitoring:ConnectivityCommandTargetMinutes", "Service level targets", "Disconnect / reconnect", "Target time for a disconnect or reconnect command to be acknowledged.", "minutes", SettingType.Integer, 1m, 10_080m),
    };

    public static SettingDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    public static bool IsEditable(string key) => Find(key) is not null;

    /// <summary>Checks a submitted value: null when it is acceptable, otherwise what is wrong. A blank value is always acceptable: it puts the setting back to its default.</summary>
    public static string? Validate(SettingDefinition d, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return $"{d.Label}: enter a number.";
        if (d.Type == SettingType.Integer && v != decimal.Truncate(v)) return $"{d.Label}: enter a whole number.";
        if (v < d.Min || v > d.Max) return $"{d.Label}: enter a value from {d.Min:0.##} to {d.Max:0.##}{(d.Unit is "%" ? "%" : " " + d.Unit)}.";
        return null;
    }

    /// <summary>The canonical text stored for a value (invariant culture, no trailing zeros).</summary>
    public static string Normalise(string raw) => decimal.Parse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture).ToString("0.##########", CultureInfo.InvariantCulture);
}
