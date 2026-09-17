namespace PrepaidEngine.Application.MeterData;

/// <summary>
/// Ingestion and cross-source validation for the MDMS profile types beyond DLP: BP (register
/// validation), LS (consumption intelligence), IP (instantaneous meter health), and Events/Alarms
/// — plus the energy-validation framework that cross-checks them against DLP. DLP ingestion and
/// billing itself remain in <c>IBillingEngineService</c>; this service never bills anything.
/// </summary>
public interface IMeterDataIngestionService
{
    Task<RegisterReadingIngestResult> IngestRegisterReadingAsync(RegisterReadingRequest request, CancellationToken cancellationToken = default);

    Task<LoadSurveyIntervalIngestResult> IngestLoadSurveyIntervalAsync(LoadSurveyIntervalRequest request, CancellationToken cancellationToken = default);

    Task<InstantaneousReadingIngestResult> IngestInstantaneousReadingAsync(InstantaneousReadingRequest request, CancellationToken cancellationToken = default);

    Task<MeterEventIngestResult> IngestMeterEventAsync(MeterEventRequest request, CancellationToken cancellationToken = default);

    Task<MeterAlarmIngestResult> IngestMeterAlarmAsync(MeterAlarmRequest request, CancellationToken cancellationToken = default);

    /// <summary>Computes (never persists) each active consumer's DLP completeness for
    /// <paramref name="profileDate"/> — see <see cref="Domain.Enums.DlpCompletenessStatus"/>.</summary>
    Task<IReadOnlyList<DlpCompletenessRow>> GetDlpCompletenessAsync(DateOnly profileDate, CancellationToken cancellationToken = default);

    /// <summary>Evaluates every rule for which both sides have data for
    /// <paramref name="consumerId"/>/<paramref name="meterId"/>/<paramref name="validationDate"/>
    /// (BP vs DLP, LS vs DLP, BP vs LS), storing/updating one <c>EnergyValidationResult</c> row
    /// per rule. Returns an empty list when no comparable data exists yet — this is never treated
    /// as a failure.</summary>
    Task<IReadOnlyList<EnergyValidationOutcome>> EvaluateEnergyValidationAsync(
        Guid consumerId, Guid meterId, DateOnly validationDate, CancellationToken cancellationToken = default);
}
