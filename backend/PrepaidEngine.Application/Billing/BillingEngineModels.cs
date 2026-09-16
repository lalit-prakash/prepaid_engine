namespace PrepaidEngine.Application.Billing;

/// <summary>An incoming Daily Load Profile.</summary>
public record DailyLoadProfileRequest(
    Guid ConsumerId,
    Guid MeterId,
    DateOnly ProfileDate,
    DateTime GeneratedAt,
    decimal StartCumulativeKwh,
    decimal EndCumulativeKwh,
    string? SourceReference = null);

/// <summary>Result of ingesting one DLP.</summary>
public record DailyLoadProfileIngestResult(Guid ProfileId, string Status, bool ReplacedProvisional, string? Message);

/// <summary>Outcome of processing one day's DLP charge for one consumer.</summary>
public record DailyProcessingResult(
    Guid ConsumerId, bool DlpAvailable, bool IsProvisional, decimal ChargeAmount, bool Skipped, string? SkipReason);

/// <summary>Request to record a physical meter replacement, per spec §16.</summary>
public record MeterReplacementRequest(
    string NewMeterNumber,
    PrepaidEngine.Domain.Enums.MeterPhase Phase,
    DateTime EffectiveFrom,
    decimal OldMeterClosingReadingKwh,
    decimal NewMeterOpeningReadingKwh,
    string Reason);
