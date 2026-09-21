using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.Connectivity;

public class DisconnectionWindowTests
{
    // IST = UTC + 5:30, so 11:00 IST is 05:30 UTC.
    [Theory]
    [InlineData(5, 29, false)]   // 10:59 IST
    [InlineData(5, 30, true)]    // 11:00 IST, the window opens
    [InlineData(9, 0, true)]     // 14:30 IST
    [InlineData(10, 29, true)]   // 15:59 IST
    [InlineData(10, 30, false)]  // 16:00 IST, the credit hours begin
    [InlineData(15, 0, false)]   // 20:30 IST
    [InlineData(20, 0, false)]   // 01:30 IST
    public void The_window_is_11am_to_4pm_India_time(int utcHour, int utcMinute, bool open)
        => Assert.Equal(open, DisconnectionWindow.IsOpen(new DateTime(2026, 9, 22, utcHour, utcMinute, 0, DateTimeKind.Utc)));

    [Fact]
    public void The_old_happy_hours_are_not_the_window()
    {
        Assert.False(DisconnectionWindow.IsOpen(new DateTime(2026, 9, 22, 4, 0, 0, DateTimeKind.Utc)));   // 09:30 IST was inside the old window
        Assert.True(DisconnectionWindow.IsOpen(new DateTime(2026, 9, 22, 8, 30, 0, DateTimeKind.Utc)));   // 14:00 IST was outside it
    }
}

public class CreditHoursTests : IDisposable
{
    private sealed class MutableClock : TimeProvider
    {
        public DateTime Utc { get; set; }
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Utc, DateTimeKind.Utc));
    }

    private sealed class AckClient : IConnectivityCommandClient
    {
        public Task<SendConnectivityCommandResult> SendConnectivityCommandAsync(SendConnectivityCommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SendConnectivityCommandResult(ConnectivityCommandOutcome.Acknowledged, null));
    }

    private static readonly DateTime Noon = new(2026, 9, 22, 6, 30, 0, DateTimeKind.Utc);    // 12:00 IST
    private static readonly DateTime Night = new(2026, 9, 22, 16, 30, 0, DateTimeKind.Utc);  // 22:00 IST

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private readonly MutableClock _clock = new() { Utc = Noon };
    private readonly EmergencyCreditGuard _guard;

    public CreditHoursTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _guard = new EmergencyCreditGuard(_db, new AckClient(), _clock, enforceCreditHours: true);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Consumer> AddConsumerAsync(string account, decimal balance)
    {
        var meter = new SmartMeter(Guid.NewGuid(), "M-" + account, MeterPhase.SinglePhase);
        var c = new Consumer(Guid.NewGuid(), account, account, "Street", meter, 1m);
        c.Wallet.SetEmergencyCreditLimit(200m);
        if (balance >= 0) c.Wallet.Credit(balance, WalletTransactionType.Recharge, "seed");
        else c.Wallet.Debit(-balance, WalletTransactionType.BillDebit, "seed");
        _db.Meters.Add(meter);
        _db.Consumers.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    [Fact]
    public async Task Inside_the_window_a_consumer_beyond_emergency_credit_is_disconnected()
    {
        var c = await AddConsumerAsync("A1", -500m);
        await _guard.EvaluateAsync(c);
        Assert.Equal(ConnectionStatus.Disconnected, c.ConnectionStatus);
    }

    [Fact]
    public async Task During_the_credit_hours_the_same_consumer_stays_connected()
    {
        _clock.Utc = Night;
        var c = await AddConsumerAsync("A2", -500m);
        await _guard.EvaluateAsync(c);
        await _db.SaveChangesAsync();
        Assert.Equal(ConnectionStatus.Active, c.ConnectionStatus);
        Assert.Empty(await _db.ConnectivityCommands.ToListAsync());
    }

    [Fact]
    public async Task Reconnection_is_never_held_back_by_the_credit_hours()
    {
        var c = await AddConsumerAsync("A3", -500m);
        await _guard.EvaluateAsync(c);
        await _db.SaveChangesAsync();
        Assert.Equal(ConnectionStatus.Disconnected, c.ConnectionStatus);

        _clock.Utc = Night;
        c.Wallet.Credit(1000m, WalletTransactionType.Recharge, "topup"); // balance is now positive
        await _guard.EvaluateAsync(c);
        Assert.Equal(ConnectionStatus.Active, c.ConnectionStatus);
    }

    [Fact]
    public async Task The_deferred_run_does_nothing_while_the_window_is_closed()
    {
        _clock.Utc = Night;
        var c = await AddConsumerAsync("B1", -500m);
        await _guard.EvaluateAsync(c); // deferred
        await _db.SaveChangesAsync();

        var handled = await new DeferredDisconnectionService(_db, _guard).RunAsync(Night);
        Assert.Equal(0, handled);
        Assert.Equal(ConnectionStatus.Active, (await _db.Consumers.AsNoTracking().SingleAsync(x => x.Id == c.Id)).ConnectionStatus);
    }

    [Fact]
    public async Task When_the_window_opens_only_the_consumers_beyond_their_limit_are_disconnected()
    {
        _clock.Utc = Night;
        var beyond = await AddConsumerAsync("B2", -500m);
        var within = await AddConsumerAsync("B3", -100m);   // inside the ₹200 emergency credit
        var healthy = await AddConsumerAsync("B4", 800m);
        await _guard.EvaluateAsync(beyond);                  // night: deferred
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _clock.Utc = Noon.AddDays(1);                        // next day, 12:00 IST
        var handled = await new DeferredDisconnectionService(_db, _guard).RunAsync(_clock.Utc);

        Assert.Equal(1, handled);
        var status = await _db.Consumers.AsNoTracking().ToDictionaryAsync(x => x.AccountNumber, x => x.ConnectionStatus);
        Assert.Equal(ConnectionStatus.Disconnected, status["B2"]);
        Assert.Equal(ConnectionStatus.Active, status["B3"]);
        Assert.Equal(ConnectionStatus.Active, status["B4"]);
        Assert.Contains(await _db.ConnectivityCommands.ToListAsync(), x => x.ConsumerId == beyond.Id && x.CommandType == ConnectivityCommandType.Disconnect);
    }

    [Fact]
    public async Task A_consumer_who_recharged_before_the_window_opened_is_left_alone()
    {
        _clock.Utc = Night;
        var c = await AddConsumerAsync("B5", -500m);
        await _guard.EvaluateAsync(c);
        _db.WalletTransactions.Add(c.Wallet.Credit(2000m, WalletTransactionType.Recharge, "topup"));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _clock.Utc = Noon.AddDays(1);
        Assert.Equal(0, await new DeferredDisconnectionService(_db, _guard).RunAsync(_clock.Utc));
    }
}
