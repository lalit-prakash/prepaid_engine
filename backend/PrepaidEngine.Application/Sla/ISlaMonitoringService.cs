namespace PrepaidEngine.Application.Sla;

/// <summary>
/// Real SLA performance for the operational workflows this engine actually times end-to-end —
/// DLP ingestion latency, daily billing-run duration, recharge completion, meter-credit
/// acknowledgement, and RC/DC command acknowledgement. Every target is configurable
/// (<c>SlaMonitoringOptions</c>, bound from appsettings.json — never hard-coded), and every
/// metric is computed from real timestamps already recorded by the entities it measures. A
/// workflow with no completed samples yet reports <c>SampleSize = 0</c> and an "Unavailable"
/// status rather than a fabricated percentage.
/// </summary>
public interface ISlaMonitoringService
{
    Task<IReadOnlyList<SlaMetric>> GetSlaSummaryAsync(CancellationToken cancellationToken = default);
}
