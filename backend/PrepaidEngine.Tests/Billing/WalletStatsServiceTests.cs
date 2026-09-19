using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrepaidEngine.Application.Wallets;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Wallets;

namespace PrepaidEngine.Tests.Billing;

public class DailyWalletStatTests
{
    [Fact]
    public void Record_sets_every_figure()
    {
        var s = new DailyWalletStat(new DateOnly(2026, 9, 1));
        var at = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        s.Record(10, 8, 2, 3, 1234.50m, at);

        Assert.Equal((10, 8, 2, 3, 1234.50m, at), (s.TotalConsumers, s.ActiveConsumers, s.DisconnectedConsumers, s.LowBalanceConsumers, s.WalletTotal, s.RecordedAt));
    }

    [Fact]
    public void Negative_counts_are_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new DailyWalletStat(new DateOnly(2026, 9, 1)).Record(-1, 0, 0, 0, 0m, DateTime.UtcNow));
}

/// <summary>The day's wallet totals are computed by the database, written once per day and overwritten by later readings.</summary>
public class WalletStatsServiceTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 9, 10);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private int _seeded;

    public WalletStatsServiceTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private WalletStatsService Service(decimal? threshold = null)
        => new(_db, Options.Create(new LowBalanceOptions { ThresholdRs = threshold }));

    /// <summary>Balance, emergency limit, disconnected?</summary>
    private async Task SeedAsync(params (decimal Balance, decimal Limit, bool Disconnected)[] wallets)
    {
        foreach (var (balance, limit, disconnected) in wallets)
        {
            var i = ++_seeded;
            var meter = new SmartMeter(Guid.NewGuid(), "M" + i, MeterPhase.SinglePhase);
            var consumer = new Consumer(Guid.NewGuid(), "A" + i, "Name " + i, "Street", meter, 1m);
            consumer.Wallet.SetEmergencyCreditLimit(limit);
            if (balance > 0) consumer.Wallet.Credit(balance, WalletTransactionType.Recharge, "seed");
            else if (balance < 0) consumer.Wallet.Debit(-balance, WalletTransactionType.BillDebit, "seed");
            if (disconnected) consumer.Disconnect();
            _db.Meters.Add(meter);
            _db.Consumers.Add(consumer);
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Counts_consumers_disconnected_low_balance_and_the_wallet_total()
    {
        // low = below the wallet's own limit: 50 < 200 yes, 500 < 200 no, -10 < 200 yes
        await SeedAsync((50m, 200m, false), (500m, 200m, false), (-10m, 200m, true));

        var stat = await Service().RecordAsync(Day, DateTime.UtcNow);

        Assert.Equal(3, stat.TotalConsumers);
        Assert.Equal(2, stat.ActiveConsumers);
        Assert.Equal(1, stat.DisconnectedConsumers);
        Assert.Equal(2, stat.LowBalanceConsumers);
        Assert.Equal(540m, stat.WalletTotal);
    }

    [Fact]
    public async Task A_configured_threshold_replaces_the_per_wallet_limit()
    {
        await SeedAsync((50m, 200m, false), (500m, 200m, false), (900m, 200m, false));

        Assert.Equal(1, (await Service(100m).RecordAsync(Day, DateTime.UtcNow)).LowBalanceConsumers);
        Assert.Equal(2, (await Service(600m).RecordAsync(Day.AddDays(1), DateTime.UtcNow)).LowBalanceConsumers);
    }

    [Fact]
    public async Task Recording_the_same_day_again_updates_the_one_row()
    {
        await SeedAsync((50m, 200m, false));
        await Service().RecordAsync(Day, DateTime.UtcNow.AddHours(-2));

        await SeedAsync((1000m, 200m, false)); // a consumer joins later in the day
        var later = await Service().RecordAsync(Day, DateTime.UtcNow);

        Assert.Equal(1, await _db.DailyWalletStats.CountAsync());
        Assert.Equal(2, later.TotalConsumers);
        Assert.Equal(2, (await _db.DailyWalletStats.SingleAsync()).TotalConsumers);
    }

    [Fact]
    public async Task Each_day_gets_its_own_row_and_an_empty_system_records_zeros()
    {
        await Service().RecordAsync(Day, DateTime.UtcNow);
        await Service().RecordAsync(Day.AddDays(1), DateTime.UtcNow);

        var rows = await _db.DailyWalletStats.OrderBy(s => s.Date).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(0, r.TotalConsumers));
    }
}
