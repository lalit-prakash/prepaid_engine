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

    /// <summary>
    /// Emergency credit (₹) this consumer's tariff category allows them to draw beyond a zero
    /// balance (e.g. MePDCL's ₹200/₹2,000 emergency credit) before being disconnect-eligible.
    /// Tracked separately from the normal wallet balance/ledger — it is an allowance, not a
    /// recharge, and is not RMS-authoritative money.
    /// </summary>
    public decimal EmergencyCreditLimit { get; private set; }

    public PrepaidWallet(Guid id, Guid consumerId)
    {
        Id = id;
        ConsumerId = consumerId;
        Balance = 0m;
        EmergencyCreditLimit = 0m;
    }

    // EF Core / serialization
    private PrepaidWallet()
    {
    }

    /// <summary>
    /// Sets the emergency credit limit applicable to this wallet, typically from the
    /// consumer's <see cref="Tariff.EmergencyCreditLimit"/> at provisioning time or whenever
    /// the consumer's tariff/category changes.
    /// </summary>
    public void SetEmergencyCreditLimit(decimal limit)
    {
        if (limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        EmergencyCreditLimit = limit;
    }

    /// <summary>
    /// True while the balance (even if negative) is still within the emergency credit
    /// allowance, i.e. supply should not yet be treated as disconnect-eligible on credit
    /// grounds alone.
    /// </summary>
    public bool IsWithinEmergencyCredit => Balance >= -EmergencyCreditLimit;

    /// <summary>
    /// Portion of the emergency credit allowance currently drawn (0 if the balance is
    /// non-negative or no emergency credit has been used).
    /// </summary>
    public decimal EmergencyCreditUsed => Balance < 0 ? Math.Min(-Balance, EmergencyCreditLimit) : 0m;

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
    /// (up to and beyond the emergency credit limit) so overdue amounts can still be tracked
    /// and trigger disconnection workflows upstream once <see cref="IsWithinEmergencyCredit"/>
    /// turns false.
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
