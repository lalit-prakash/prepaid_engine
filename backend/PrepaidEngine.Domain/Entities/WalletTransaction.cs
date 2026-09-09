using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// An immutable ledger entry recording a single movement against a <see cref="PrepaidWallet"/> balance.
/// </summary>
public class WalletTransaction
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }

    /// <summary>Signed amount: positive for credits, negative for debits.</summary>
    public decimal Amount { get; private set; }
    public WalletTransactionType Type { get; private set; }
    public DateTime OccurredAt { get; private set; }

    /// <summary>Optional external reference, e.g. an RMS recharge/bill id.</summary>
    public string? Reference { get; private set; }

    public WalletTransaction(Guid id, Guid walletId, decimal amount, WalletTransactionType type, DateTime occurredAt, string? reference)
    {
        Id = id;
        WalletId = walletId;
        Amount = amount;
        Type = type;
        OccurredAt = occurredAt;
        Reference = reference;
    }

    // EF Core / serialization
    private WalletTransaction()
    {
    }
}
