using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// One 30-minute Load Survey (LS) block from a smart meter — near-real-time interval data used
/// to debit the prepaid wallet hourly. Deliberately a distinct entity from
/// <see cref="ConsumptionReading"/> (a generic consumption/billing record) and from
/// <see cref="DailyLoadProfile"/> (the daily meter profile created at the 00:00 boundary, used
/// for the authoritative daily charge): LS is an interval stream with its own validation state,
/// source identity, and interval-level idempotency, and must never be modeled as or confused with
/// either.
///
/// Two completed 30-minute blocks form one billable hour — see
/// <c>IBillingEngineService.ProcessCompletedHourAsync</c>. A block is immutable and idempotent
/// once received: uniqueness is enforced on <c>MeterId + IntervalStart + IntervalEnd</c>, and the
/// financial ledger additionally uses the reference <c>LS:&lt;interval-id&gt;</c> so the same
/// block can never create two wallet debits, even if the hourly job runs twice.
/// </summary>
public class LoadSurveyInterval
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid MeterId { get; private set; }
    public DateTime IntervalStart { get; private set; }
    public DateTime IntervalEnd { get; private set; }
    public decimal CumulativeKwh { get; private set; }
    public decimal IntervalKwh { get; private set; }
    public LoadSurveyQuality Quality { get; private set; }
    public LoadSurveyStatus Status { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public string? SourceReference { get; private set; }

    public LoadSurveyInterval(
        Guid id,
        Guid consumerId,
        Guid meterId,
        DateTime intervalStart,
        DateTime intervalEnd,
        decimal cumulativeKwh,
        decimal intervalKwh,
        DateTime receivedAt,
        string? sourceReference = null)
    {
        if (intervalEnd <= intervalStart)
            throw new ArgumentException("Interval end must be after interval start.", nameof(intervalEnd));
        if (intervalEnd - intervalStart != TimeSpan.FromMinutes(30))
            throw new ArgumentException("A Load Survey block must span exactly 30 minutes.", nameof(intervalEnd));
        if (cumulativeKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(cumulativeKwh), "Cumulative reading cannot be negative.");

        Id = id;
        ConsumerId = consumerId;
        MeterId = meterId;
        IntervalStart = intervalStart;
        IntervalEnd = intervalEnd;
        CumulativeKwh = cumulativeKwh;
        IntervalKwh = intervalKwh;
        Quality = LoadSurveyQuality.Valid;
        Status = LoadSurveyStatus.Received;
        ReceivedAt = receivedAt;
        SourceReference = sourceReference;
    }

    // EF Core / serialization
    private LoadSurveyInterval()
    {
    }

    /// <summary>Marks the block Valid/Validated — safe to use for hourly billing.</summary>
    public void MarkValidated()
    {
        Quality = LoadSurveyQuality.Valid;
        Status = LoadSurveyStatus.Validated;
    }

    /// <summary>Marks the block rejected with a specific data-quality reason (e.g.
    /// <see cref="LoadSurveyQuality.NegativeConsumption"/>, <see cref="LoadSurveyQuality.Duplicate"/>,
    /// <see cref="LoadSurveyQuality.OutOfSequence"/>) — never silently converted to zero
    /// consumption, per the spec's core rule.</summary>
    public void MarkRejected(LoadSurveyQuality quality)
    {
        if (quality == LoadSurveyQuality.Valid)
            throw new ArgumentException("Valid is not a rejection reason.", nameof(quality));

        Quality = quality;
        Status = LoadSurveyStatus.Rejected;
    }

    /// <summary>Marks the block Processed once its hourly wallet debit has been posted.</summary>
    public void MarkProcessed()
    {
        if (Status != LoadSurveyStatus.Validated)
            throw new InvalidOperationException($"Cannot process a Load Survey block that is {Status} — only a Validated block can be processed.");

        Status = LoadSurveyStatus.Processed;
    }
}
