using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A recharge request processed through RMS (Recharge Management System) that,
/// once successful, credits the consumer's <see cref="PrepaidWallet"/>.
/// </summary>
public class RechargeTransaction
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public decimal Amount { get; private set; }
    public string RmsReferenceId { get; private set; }
    public RechargeStatus Status { get; private set; }
    public DateTime InitiatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public RechargeTransaction(Guid id, Guid consumerId, decimal amount, string rmsReferenceId, DateTime initiatedAt)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(rmsReferenceId))
            throw new ArgumentException("RMS reference id is required.", nameof(rmsReferenceId));

        Id = id;
        ConsumerId = consumerId;
        Amount = amount;
        RmsReferenceId = rmsReferenceId;
        Status = RechargeStatus.Initiated;
        InitiatedAt = initiatedAt;
    }

    // EF Core / serialization
    private RechargeTransaction()
    {
        RmsReferenceId = string.Empty;
    }

    public void MarkSuccessful(DateTime completedAt)
    {
        if (Status != RechargeStatus.Initiated)
            throw new InvalidOperationException($"Cannot mark a {Status} recharge as successful.");

        Status = RechargeStatus.Success;
        CompletedAt = completedAt;
    }

    public void MarkFailed(DateTime completedAt)
    {
        if (Status != RechargeStatus.Initiated)
            throw new InvalidOperationException($"Cannot mark a {Status} recharge as failed.");

        Status = RechargeStatus.Failed;
        CompletedAt = completedAt;
    }

    public void Reverse(DateTime reversedAt)
    {
        if (Status != RechargeStatus.Success)
            throw new InvalidOperationException("Only a successful recharge can be reversed.");

        Status = RechargeStatus.Reversed;
        CompletedAt = reversedAt;
    }
}
