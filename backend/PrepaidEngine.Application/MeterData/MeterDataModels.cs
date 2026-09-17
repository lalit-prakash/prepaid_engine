using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Application.MeterData;

/// <summary>An incoming Billing Profile (BP) register reading.</summary>
public record RegisterReadingRequest(
    Guid ConsumerId, Guid MeterId, DateTime ReadingTimestamp, decimal CumulativeImportKwh, string? SourceReference = null);

public record RegisterReadingIngestResult(Guid ReadingId, string Status, string? Message);

/// <summary>An incoming Load Survey (LS) interval — consumption intelligence only, never a
/// billing input (see <see cref="Domain.Entities.LoadSurveyInterval"/>).</summary>
public record LoadSurveyIntervalRequest(
    Guid ConsumerId, Guid MeterId, DateTime IntervalStart, DateTime IntervalEnd, decimal ImportKwh, string? SourceReference = null);

public record LoadSurveyIntervalIngestResult(Guid IntervalId, string Status, string? Message);

/// <summary>An incoming Instantaneous Profile (IP) reading.</summary>
public record InstantaneousReadingRequest(
    Guid ConsumerId,
    Guid MeterId,
    DateTime Timestamp,
    decimal VoltageVolts,
    decimal CurrentAmps,
    decimal PowerKw,
    decimal PowerFactor,
    decimal FrequencyHz,
    MeterRelayStatus RelayStatus,
    string? SourceReference = null);

public record InstantaneousReadingIngestResult(Guid ReadingId, string Status, string? Message);

/// <summary>An incoming informational meter event.</summary>
public record MeterEventRequest(
    Guid ConsumerId, Guid MeterId, MeterEventCode EventCode, DateTime EventTimestamp, string? Description = null, string? SourceReference = null);

public record MeterEventIngestResult(Guid EventId, string Status, string? Message);

/// <summary>An incoming alarm-worthy meter condition.</summary>
public record MeterAlarmRequest(
    Guid ConsumerId, Guid MeterId, MeterAlarmCode AlarmCode, MeterAlarmSeverity Severity, DateTime RaisedAt, string? SourceReference = null);

public record MeterAlarmIngestResult(Guid AlarmId, string Status, string? Message);

/// <summary>One consumer/meter's DLP completeness for a given date — computed, never stored
/// (see <see cref="Domain.Enums.DlpCompletenessStatus"/>).</summary>
public record DlpCompletenessRow(Guid ConsumerId, string AccountNumber, Guid MeterId, string MeterNumber, DateOnly ProfileDate, DlpCompletenessStatus Status);

/// <summary>Result of evaluating one energy-validation rule for one consumer/meter/day.</summary>
public record EnergyValidationOutcome(
    Guid ResultId,
    Guid ConsumerId,
    Guid MeterId,
    DateOnly ValidationDate,
    EnergyValidationRule Rule,
    decimal ExpectedValueKwh,
    decimal ActualValueKwh,
    decimal VarianceKwh,
    decimal VariancePct,
    EnergyValidationStatus Status,
    string Reason);
