namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// One row per day of how the wallet base looked: how many consumers, how many disconnected, how many low on balance, and
/// the total wallet balance. It is refreshed through the day and left as the day's last reading, so low-balance and
/// disconnection trends can be charted. It holds totals only, never one row per consumer, so it stays small at any
/// population size. History starts at the first recorded day; nothing is back-filled, because past balances were not kept.
/// </summary>
public class DailyWalletStat
{
    public DateOnly Date { get; private set; }
    public int TotalConsumers { get; private set; }
    public int ActiveConsumers { get; private set; }
    public int DisconnectedConsumers { get; private set; }
    public int LowBalanceConsumers { get; private set; }
    public decimal WalletTotal { get; private set; }
    public DateTime RecordedAt { get; private set; }

    public DailyWalletStat(DateOnly date) => Date = date;

    // EF Core / serialization
    private DailyWalletStat() { }

    public void Record(int total, int active, int disconnected, int lowBalance, decimal walletTotal, DateTime recordedAt)
    {
        if (total < 0 || active < 0 || disconnected < 0 || lowBalance < 0)
            throw new ArgumentOutOfRangeException(nameof(total), "Counts cannot be negative.");

        TotalConsumers = total;
        ActiveConsumers = active;
        DisconnectedConsumers = disconnected;
        LowBalanceConsumers = lowBalance;
        WalletTotal = walletTotal;
        RecordedAt = recordedAt;
    }
}
