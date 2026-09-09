using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// Holds a consumer's prepaid balance and the ledger of movements against it.
/// Recharges (via RMS) credit the wallet; bill generation debits it.
/// </summary>
public class PrepaidWallet
{
    private readonly List<WalletTransaction> _transactions = new();

    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public decimal Balance { get; private set; }
    public IReadOnlyCollection<WalletTransaction> Transactions => _transactions.AsReadOnly();

    public PrepaidWallet(Guid id, Guid consumerId)
    {
        Id = id;
        ConsumerId = consumerId;
        Balance = 0m;
    }

    // EF Core / serialization
    private PrepaidWallet()
    {
    }

    /// <summary>
    /// Credits the wallet, e.g. following a successful RMS recharge.
    /// </summary>
    public WalletTransaction Credit(decimal amount, WalletTransactionType type, string? reference = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount must be positive.");

        Balance += amount;
        var transaction = new WalletTransaction(Guid.NewGuid(), Id, amount, type, DateTime.UtcNow, reference);
        _transactions.Add(transaction);
        return transaction;
    }

    /// <summary>
    /// Debits the wallet, e.g. to settle a generated bill. Allows the balance to go negative
    /// so overdue amounts can still be tracked and trigger disconnection workflows upstream.
    /// </summary>
    public WalletTransaction Debit(decimal amount, WalletTransactionType type, string? reference = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Debit amount must be positive.");

        Balance -= amount;
        var transaction = new WalletTransaction(Guid.NewGuid(), Id, -amount, type, DateTime.UtcNow, reference);
        _transactions.Add(transaction);
        return transaction;
    }
}
