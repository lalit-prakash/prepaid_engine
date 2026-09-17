namespace PrepaidEngine.Domain.Enums;

/// <summary>Computed (never stored) daily-profile completeness for one consumer/meter/date —
/// see <c>MeterDataIngestionService.GetDlpCompletenessAsync</c>. Influences billing eligibility:
/// only <see cref="Complete"/> and <see cref="Provisional"/> are billable as-is; the others
/// surface as a <c>MeterBillingControl</c> hold reason.</summary>
public enum DlpCompletenessStatus
{
    Complete = 0,
    Partial = 1,
    Missing = 2,
    Invalid = 3,
    Duplicate = 4,
    Provisional = 5,
}
