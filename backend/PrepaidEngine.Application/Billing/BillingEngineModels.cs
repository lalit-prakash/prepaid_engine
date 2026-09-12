namespace PrepaidEngine.Application.Billing;

/// <summary>One incoming 30-minute Load Survey block, per the LS/DLP billing pipeline spec §24.</summary>
public record LoadSurveyBlockRequest(
    Guid ConsumerId,
    Guid MeterId,
    DateTime IntervalStart,
    DateTime IntervalEnd,
    decimal CumulativeKwh,
    decimal IntervalKwh,
    string? SourceReference = null);

/// <summary>Result of ingesting one LS block.</summary>
public record LoadSurveyIngestResult(Guid IntervalId, string Quality, string Status, string? Message);

/// <summary>An incoming Daily Load Profile, per spec §24.</summary>
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

/// <summary>Outcome of processing one completed hour for one consumer.</summary>
public record HourlyProcessingResult(Guid ConsumerId, int BlocksProcessed, decimal TotalCharge, bool Skipped, string? SkipReason);

/// <summary>Outcome of processing one day's DLP settlement for one consumer.</summary>
public record DailyProcessingResult(
    Guid ConsumerId, bool DlpAvailable, bool IsProvisional, decimal AuthoritativeCharge,
    decimal LsDebitTotal, decimal ProvisionalDebit, decimal Settlement, bool Skipped, string? SkipReason);

/// <summary>Request to record a physical meter replacement, per spec §16.</summary>
public record MeterReplacementRequest(
    string NewMeterNumber,
    PrepaidEngine.Domain.Enums.MeterPhase Phase,
    DateTime EffectiveFrom,
    decimal OldMeterClosingReadingKwh,
    decimal NewMeterOpeningReadingKwh,
    string Reason);
