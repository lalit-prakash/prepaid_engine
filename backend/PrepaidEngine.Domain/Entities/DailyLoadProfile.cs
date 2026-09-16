using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A Daily Load Profile (DLP) — the meter's daily consumption profile, created at the 00:00 hrs
/// boundary, used to compute the day's direct wallet charge (see
/// <c>BillingEngineService.ProcessDailyAsync</c>). This is now the sole driver of ongoing prepaid
/// billing — the 30-minute Load Survey (LS) stream this project used to also bill from, and
/// settle DLP against, has been removed.
///
/// Unique per <c>ConsumerId + MeterId + ProfileDate</c>. When the DLP for a date is missing at
/// the daily billing boundary, a provisional profile is created instead (<see cref="IsProvisional"/>)
/// and later replaced in place by the actual DLP via <see cref="ReplaceWithActual"/> — the
/// historical provisional wallet transaction is never edited.
/// </summary>
public class DailyLoadProfile
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid MeterId { get; private set; }
    public DateOnly ProfileDate { get; private set; }
    public DateTime GeneratedAt { get; private set; }
    public decimal StartCumulativeKwh { get; private set; }
    public decimal EndCumulativeKwh { get; private set; }
    public decimal TotalKwh { get; private set; }
    public DailyProfileStatus Status { get; private set; }
    public bool IsProvisional { get; private set; }
    public string? SourceReference { get; private set; }

    public DailyLoadProfile(
        Guid id,
        Guid consumerId,
        Guid meterId,
        DateOnly profileDate,
        DateTime generatedAt,
        decimal startCumulativeKwh,
        decimal endCumulativeKwh,
        bool isProvisional = false,
        string? sourceReference = null)
    {
        if (endCumulativeKwh < startCumulativeKwh)
            throw new ArgumentOutOfRangeException(nameof(endCumulativeKwh), "End cumulative reading cannot be lower than the start reading.");

        Id = id;
        ConsumerId = consumerId;
        MeterId = meterId;
        ProfileDate = profileDate;
        GeneratedAt = generatedAt;
        StartCumulativeKwh = startCumulativeKwh;
        EndCumulativeKwh = endCumulativeKwh;
        TotalKwh = endCumulativeKwh - startCumulativeKwh;
        Status = isProvisional ? DailyProfileStatus.Provisional : DailyProfileStatus.Received;
        IsProvisional = isProvisional;
        SourceReference = sourceReference;
    }

    /// <summary>Creates a provisional DLP from an estimated daily consumption (spec §13.1: demo
    /// estimate is the average of up to the previous 7 valid DLP records) — no real meter data
    /// exists for this date yet.</summary>
    public static DailyLoadProfile CreateProvisional(
        Guid id, Guid consumerId, Guid meterId, DateOnly profileDate, DateTime generatedAt, decimal estimatedKwh)
    {
        if (estimatedKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedKwh), "Estimated consumption cannot be negative.");

        return new DailyLoadProfile(id, consumerId, meterId, profileDate, generatedAt, 0m, estimatedKwh, isProvisional: true);
    }

    // EF Core / serialization
    private DailyLoadProfile()
    {
    }

    public void MarkValidated()
    {
        if (IsProvisional)
            throw new InvalidOperationException("Cannot validate a provisional profile — it has no real meter data. Use ReplaceWithActual instead.");

        Status = DailyProfileStatus.Validated;
    }

    public void MarkRejected()
    {
        Status = DailyProfileStatus.Rejected;
    }

    public void MarkBilled()
    {
        Status = DailyProfileStatus.Billed;
    }

    /// <summary>Replaces a provisional profile's data with the actual meter profile once it
    /// arrives — the historical provisional wallet transaction is untouched; only the daily
    /// settlement calculation (run again) accounts for the corrected total.</summary>
    public void ReplaceWithActual(decimal startCumulativeKwh, decimal endCumulativeKwh, DateTime generatedAt, string? sourceReference)
    {
        if (!IsProvisional)
            throw new InvalidOperationException("Only a provisional profile can be replaced with actual data.");
        if (endCumulativeKwh < startCumulativeKwh)
            throw new ArgumentOutOfRangeException(nameof(endCumulativeKwh), "End cumulative reading cannot be lower than the start reading.");

        StartCumulativeKwh = startCumulativeKwh;
        EndCumulativeKwh = endCumulativeKwh;
        TotalKwh = endCumulativeKwh - startCumulativeKwh;
        GeneratedAt = generatedAt;
        SourceReference = sourceReference;
        IsProvisional = false;
        Status = DailyProfileStatus.Validated;
    }
}
