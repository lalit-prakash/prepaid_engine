namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Lifecycle status of a generated <see cref="Entities.PrepaidBill"/>.
/// </summary>
public enum BillStatus
{
    Generated,
    Paid,
    PartiallyPaid,
    Overdue,
    Cancelled
}
