namespace PrepaidEngine.Domain.Enums;

/// <summary>Status of a <see cref="Entities.BillingRun"/> batch (e.g. one day's DLP settlement run).</summary>
public enum BillingRunStatus
{
    Running,
    Completed,
    CompletedWithExceptions,
    Failed,
}
