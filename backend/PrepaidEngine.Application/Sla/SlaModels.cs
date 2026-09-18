namespace PrepaidEngine.Application.Sla;

/// <summary>One configured SLA's real performance — see <see cref="ISlaMonitoringService"/>.
/// <paramref name="SampleSize"/> of 0 means "Data unavailable", never a fabricated 100%.</summary>
public record SlaMetric(
    string Name,
    decimal TargetMinutes,
    decimal? ActualAverageMinutes,
    int SampleSize,
    int BreachCount,
    decimal? BreachPercentage,
    string Status);
