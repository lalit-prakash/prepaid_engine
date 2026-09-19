using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Application.Wallets;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.Billing;

public class LowBalanceOptionsTests
{
    [Fact]
    public void Unset_keeps_the_defaults()
    {
        var o = new LowBalanceOptions();
        Assert.Null(o.ThresholdRs);
        Assert.Equal(100m, o.NotificationThreshold);
        Assert.True(o.IsValid);
    }

    [Fact]
    public void A_configured_figure_becomes_the_notification_threshold()
        => Assert.Equal(250m, new LowBalanceOptions { ThresholdRs = 250m }.NotificationThreshold);

    [Theory]
    [InlineData(0, true)]
    [InlineData(500, true)]
    [InlineData(-1, false)]
    public void Negative_figures_are_not_valid(int value, bool valid)
        => Assert.Equal(valid, new LowBalanceOptions { ThresholdRs = value }.IsValid);
}

/// <summary>The daily billing run warns a consumer whose balance falls below the configured low-balance figure.</summary>
public class LowBalanceNotificationTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 9, 10);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;

    public LowBalanceNotificationTests()
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

    private sealed class AckClient : IConnectivityCommandClient
    {
        public Task<SendConnectivityCommandResult> SendConnectivityCommandAsync(SendConnectivityCommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SendConnectivityCommandResult(ConnectivityCommandOutcome.Acknowledged, null));
    }

    /// <summary>One consumer with Rs.400 who is billed Rs.50 for the day, leaving Rs.350.</summary>
    private async Task<int> LowBalanceNotificationsAfterBillingAsync(decimal? threshold)
    {
        var tariff = new Tariff(Guid.NewGuid(), "Flat", ConsumerCategory.Domestic, new[] { new TariffSlab(0, null, 5m) }, 0m, 0m);
        var meter = new SmartMeter(Guid.NewGuid(), "M-LB", MeterPhase.SinglePhase);
        var consumer = new Consumer(Guid.NewGuid(), "LB-1", "Low balance test", "Street", meter, 1m);
        consumer.AssignTariff(tariff.Id);
        consumer.Wallet.SetEmergencyCreditLimit(200m);
        consumer.Wallet.Credit(400m, WalletTransactionType.Recharge, "seed");
        _db.Tariffs.Add(tariff);
        _db.Meters.Add(meter);
        _db.Consumers.Add(consumer);
        _db.DailyLoadProfiles.Add(new DailyLoadProfile(Guid.NewGuid(), consumer.Id, meter.Id, Day, DateTime.UtcNow.AddHours(-2), 1000m, 1010m, DateTime.UtcNow.AddHours(-2)));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var service = new BillingEngineService(_db, new EmergencyCreditGuard(_db, new AckClient()),
            Options.Create(new LowBalanceOptions { ThresholdRs = threshold }));
        await service.ProcessDailyStage1Async(Day, DateTime.UtcNow.AddHours(1));

        return await _db.NotificationEvents.CountAsync(n => n.EventType == NotificationEventType.LowBalance);
    }

    [Fact]
    public async Task By_default_a_balance_of_350_is_not_low()
        => Assert.Equal(0, await LowBalanceNotificationsAfterBillingAsync(null));

    [Fact]
    public async Task A_higher_configured_threshold_makes_350_low_and_queues_the_warning()
        => Assert.Equal(1, await LowBalanceNotificationsAfterBillingAsync(500m));

    [Fact]
    public async Task A_lower_configured_threshold_keeps_350_quiet()
        => Assert.Equal(0, await LowBalanceNotificationsAfterBillingAsync(300m));
}
