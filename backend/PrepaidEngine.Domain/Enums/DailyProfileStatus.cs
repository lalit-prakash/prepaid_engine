namespace PrepaidEngine.Domain.Enums;

/// <summary>Processing status of a <see cref="Entities.DailyLoadProfile"/>.</summary>
public enum DailyProfileStatus
{
    Received,
    Validated,
    Rejected,
    Billed,
    Provisional,
    Reconciled,
}
