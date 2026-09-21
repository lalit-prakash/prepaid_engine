namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A Load Survey (LS) interval reading — MDMS's sub-daily consumption stream, re-introduced here
/// strictly as consumption INTELLIGENCE: load pattern, peak demand, average consumption,
/// consumption-anomaly detection, and low-balance depletion forecasting. LS is explicitly NOT a
/// billing input — <see cref="DailyLoadProfile"/> alone drives ongoing prepaid billing (an
/// earlier hourly-LS billing pipeline in this project was removed for exactly that reason: never
/// let two sources bill the same consumption). LS is only ever cross-checked against DLP for
/// energy validation (<c>EnergyValidationRule.LsVsDlp</c>/<c>BpVsLs</c>), never charged directly.
///
/// The interval size is whatever MDMS actually reports per push — this entity does not assume a
/// fixed 15/30-minute cadence; <see cref="IntervalStart"/>/<see cref="IntervalEnd"/> carry the
/// real boundaries so aggregation can be interval-size-agnostic.
///
/// Unique per <c>ConsumerId + MeterId + IntervalStart</c>.
/// </summary>
public class LoadSurveyInterval
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid MeterId { get; private set; }
    public DateTime IntervalStart { get; private set; }
    public DateTime IntervalEnd { get; private set; }
    public decimal ImportKwh { get; private set; }

    /// <summary>Apparent energy in the interval, when the meter reports it. Used, with the interval's time, to split a Time-of-Day consumer's day into bands.</summary>
    public decimal? ImportKvah { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public string? SourceReference { get; private set; }

    public LoadSurveyInterval(
        Guid id,
        Guid consumerId,
        Guid meterId,
        DateTime intervalStart,
        DateTime intervalEnd,
        decimal importKwh,
        DateTime receivedAt,
        string? sourceReference = null,
        decimal? importKvah = null)
    {
        if (importKvah < 0)
            throw new ArgumentOutOfRangeException(nameof(importKvah), "Interval consumption cannot be negative.");
        if (intervalEnd <= intervalStart)
            throw new ArgumentOutOfRangeException(nameof(intervalEnd), "Interval end must be after interval start.");
        if (importKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(importKwh), "Interval consumption cannot be negative.");

        Id = id;
        ConsumerId = consumerId;
        MeterId = meterId;
        IntervalStart = intervalStart;
        IntervalEnd = intervalEnd;
        ImportKwh = importKwh;
        ImportKvah = importKvah;
        ReceivedAt = receivedAt;
        SourceReference = sourceReference;
    }

    // EF Core / serialization
    private LoadSurveyInterval()
    {
    }
}
