using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A bill generated for a consumer from a consumption reading and the applicable tariff.
/// </summary>
public class PrepaidBill
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid ConsumptionReadingId { get; private set; }
    public Guid TariffId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal AmountPaid { get; private set; }
    public DateTime GeneratedAt { get; private set; }
    public BillStatus Status { get; private set; }

    public PrepaidBill(Guid id, Guid consumerId, Guid consumptionReadingId, Guid tariffId, decimal amount, DateTime generatedAt)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        Id = id;
        ConsumerId = consumerId;
        ConsumptionReadingId = consumptionReadingId;
        TariffId = tariffId;
        Amount = amount;
        AmountPaid = 0m;
        GeneratedAt = generatedAt;
        Status = BillStatus.Generated;
    }

    // EF Core / serialization
    private PrepaidBill()
    {
    }

    public decimal OutstandingAmount => Math.Max(0m, Amount - AmountPaid);

    /// <summary>
    /// Applies a payment (typically a wallet debit settling this bill) and updates status.
    /// </summary>
    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (Status is BillStatus.Cancelled)
            throw new InvalidOperationException("Cannot apply payment to a cancelled bill.");

        AmountPaid += amount;
        Status = OutstandingAmount <= 0m ? BillStatus.Paid : BillStatus.PartiallyPaid;
    }

    public void MarkOverdue()
    {
        if (Status is BillStatus.Generated or BillStatus.PartiallyPaid)
            Status = BillStatus.Overdue;
    }

    public void Cancel()
    {
        if (Status == BillStatus.Paid)
            throw new InvalidOperationException("Cannot cancel a paid bill.");

        Status = BillStatus.Cancelled;
    }
}
