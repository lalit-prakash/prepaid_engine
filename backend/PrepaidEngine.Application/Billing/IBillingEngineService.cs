using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Application.Billing;

/// <summary>
/// The LS/DLP billing pipeline's four core operations, per
/// "Prepaid_Engine_Full_RnD_and_Code_Change_Spec" (v2.0, 11-09-2026). See that spec's section 1
/// for the core rule this interface exists to enforce: LS (30-minute Load Survey) drives hourly
/// wallet debits, DLP (Daily Load Profile, created at the 00:00 boundary) drives the
/// authoritative daily charge, and the two are reconciled via a signed settlement adjustment —
/// the wallet must never be debited twice for the same consumption.
/// </summary>
public interface IBillingEngineService
{
    /// <summary>Ingests a batch of 30-minute LS blocks: validates duration/duplicate/sequence,
    /// classifies data quality, and — for <see cref="Domain.Enums.LoadSurveyQuality.NegativeConsumption"/> —
    /// activates a <see cref="MeterBillingControl"/> hold rather than silently zeroing the block.</summary>
    Task<IReadOnlyList<LoadSurveyIngestResult>> IngestLoadSurveyAsync(
        IReadOnlyList<LoadSurveyBlockRequest> blocks, CancellationToken cancellationToken = default);

    /// <summary>Ingests one Daily Load Profile — replaces an existing provisional profile for the
    /// same Consumer+Meter+Date if one exists, otherwise creates a new (real) profile.</summary>
    Task<DailyLoadProfileIngestResult> IngestDailyLoadProfileAsync(
        DailyLoadProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Processes every consumer's completed hour ending at <paramref name="hourEndUtc"/>:
    /// finds the two validated LS blocks forming that hour, calculates the hourly energy charge
    /// from the consumer's assigned tariff, and posts an idempotent wallet debit per block. Safe
    /// to re-run for the same hour (already-Processed blocks are skipped, and the wallet
    /// transaction reference is itself a database-enforced idempotency key).</summary>
    Task<IReadOnlyList<HourlyProcessingResult>> ProcessCompletedHourAsync(
        DateTime hourEndUtc, CancellationToken cancellationToken = default);

    /// <summary>Processes the daily DLP settlement for <paramref name="billingDate"/> across every
    /// prepaid consumer with an assigned tariff: computes the authoritative daily charge from the
    /// day's DLP (or creates+charges a provisional profile if none arrived), settles it against
    /// the day's hourly LS debits and any prior provisional debit, and posts only the signed
    /// difference. Tracked under one <see cref="BillingRun"/> (unique per RunType+BillingDate).</summary>
    Task<IReadOnlyList<DailyProcessingResult>> ProcessDailyAsync(
        DateOnly billingDate, CancellationToken cancellationToken = default);

    /// <summary>Records a physical meter replacement for a consumer: creates the new
    /// <see cref="Domain.Entities.SmartMeter"/>, swaps it onto the consumer, and writes the
    /// <see cref="MeterAssignment"/> audit record — old and new meter cumulative readings are
    /// never compared against each other.</summary>
    Task<MeterAssignment> ReplaceMeterAsync(
        Guid consumerId, MeterReplacementRequest request, CancellationToken cancellationToken = default);
}
