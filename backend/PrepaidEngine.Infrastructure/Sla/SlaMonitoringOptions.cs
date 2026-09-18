namespace PrepaidEngine.Infrastructure.Sla;

/// <summary>Configurable SLA targets, in minutes — bound from the "SlaMonitoring" section of
/// appsettings.json. Never hard-coded in the measurement logic itself.</summary>
public class SlaMonitoringOptions
{
    public const string SectionName = "SlaMonitoring";

    public decimal DlpIngestionTargetMinutes { get; set; } = 120m;
    public decimal BillingRunTargetMinutes { get; set; } = 30m;
    public decimal RechargeCompletionTargetMinutes { get; set; } = 5m;
    public decimal MeterCreditTargetMinutes { get; set; } = 5m;
    public decimal ConnectivityCommandTargetMinutes { get; set; } = 10m;
}
