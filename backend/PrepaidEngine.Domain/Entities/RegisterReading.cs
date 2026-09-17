using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A Billing Profile (BP) register reading — MDMS's own periodic cumulative-energy snapshot for
/// a meter's register, used to validate the <see cref="DailyLoadProfile"/> stream against the
/// meter's actual register rather than trusting DLP alone (see
/// <c>MeterDataIngestionService.EvaluateEnergyValidationAsync</c>, rule
/// <see cref="EnergyValidationRule.BpVsDlp"/>). BP is never used to drive billing directly — DLP
/// remains the sole daily billing source; BP only confirms or disputes it.
///
/// Unique per <c>ConsumerId + MeterId + ReadingTimestamp</c> — a duplicate ingest with the same
/// key is rejected rather than silently re-processed (see the unique index in
/// <c>RegisterReadingConfiguration</c>).
/// </summary>
public class RegisterReading
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid MeterId { get; private set; }

    /// <summary>The register's own reading timestamp (meter/head-end clock) — distinct from
    /// <see cref="ReceivedAt"/>.</summary>
    public DateTime ReadingTimestamp { get; private set; }

    public decimal CumulativeImportKwh { get; private set; }
    public RegisterReadingStatus Status { get; private set; }

    /// <summary>Server-set ingestion time — never caller-supplied, matching the
    /// <see cref="DailyLoadProfile.ReceivedAt"/> convention.</summary>
    public DateTime ReceivedAt { get; private set; }

    public string? SourceReference { get; private set; }

    public RegisterReading(
        Guid id,
        Guid consumerId,
        Guid meterId,
        DateTime readingTimestamp,
        decimal cumulativeImportKwh,
        DateTime receivedAt,
        string? sourceReference = null)
    {
        if (cumulativeImportKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(cumulativeImportKwh), "Cumulative reading cannot be negative.");

        Id = id;
        ConsumerId = consumerId;
        MeterId = meterId;
        ReadingTimestamp = readingTimestamp;
        CumulativeImportKwh = cumulativeImportKwh;
        ReceivedAt = receivedAt;
        SourceReference = sourceReference;
        Status = RegisterReadingStatus.Received;
    }

    // EF Core / serialization
    private RegisterReading()
    {
    }

    public void MarkValidated() => Status = RegisterReadingStatus.Validated;

    public void MarkRejected() => Status = RegisterReadingStatus.Rejected;
}
