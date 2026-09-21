namespace PrepaidEngine.Application.Billing;

/// <summary>An incoming Daily Load Profile.</summary>
public record DailyLoadProfileRequest(
    Guid ConsumerId,
    Guid MeterId,
    DateOnly ProfileDate,
    DateTime GeneratedAt,
    decimal StartCumulativeKwh,
    decimal EndCumulativeKwh,
    string? SourceReference = null,
    decimal? StartCumulativeKvah = null,
    decimal? EndCumulativeKvah = null);

/// <summary>Result of ingesting one DLP. <paramref name="Message"/> carries the rejection reason
/// when <paramref name="Status"/> is "Rejected" — e.g. its starting reading is lower than the
/// meter's previous day's closing reading, a negative-consumption sequence this project never
/// bills against (mirrors the old Load Survey pipeline's own negative-consumption guard, now
/// applied to DLP instead). A rejection also raises/reactivates a MeterBillingControl hold for
/// the meter, blocking further DLP billing until an operator clears it.</summary>
public record DailyLoadProfileIngestResult(Guid ProfileId, string Status, bool ReplacedProvisional, string? Message);

/// <summary>Outcome of processing one day's DLP charge for one consumer, in a given billing stage
/// ("Stage1", "Stage2", or "Stage2Provisional").</summary>
public record DailyProcessingResult(
    Guid ConsumerId, string Stage, bool DlpAvailable, bool IsProvisional, decimal ChargeAmount, bool Skipped, string? SkipReason);

/// <summary>Request to record a physical meter replacement, per spec §16.</summary>
public record MeterReplacementRequest(
    string NewMeterNumber,
    PrepaidEngine.Domain.Enums.MeterPhase Phase,
    DateTime EffectiveFrom,
    decimal OldMeterClosingReadingKwh,
    decimal NewMeterOpeningReadingKwh,
    string Reason);
