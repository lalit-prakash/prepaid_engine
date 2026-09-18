namespace PrepaidEngine.Domain.Enums;

/// <summary>Lifecycle of a <see cref="Entities.TariffChangeRequest"/>. <c>Approved</c> and
/// <c>Scheduled</c> are folded into one transition (<c>Approve()</c> always requires and records
/// a commencement date in the same step) — see the entity's own doc comment.</summary>
public enum TariffChangeRequestStatus
{
    Draft = 0,
    PendingApproval = 1,
    Rejected = 2,
    Scheduled = 3,
    Activated = 4,
    Cancelled = 5,
}
