using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Application.Connectivity;

namespace PrepaidEngine.Tests.Billing;

/// <summary>
/// The daily billing stages run in batches, are claimed by one instance at a time, and can be resumed after a
/// crash without billing anyone twice. These tests cover that, using the same lightweight SQLite database as
/// <see cref="BillingEngineServiceTests"/>.
/// </summary>
public class BillingBatchRunTests : IDisposable
{
    private static readonly DateOnly BillingDate = new(2026, 9, 10);

    private readonly SqliteConnection _connection;
    private readonly PrepaidEngineDbContext _db;
    private readonly BillingEngineService _service;
    private readonly Tariff _tariff;

    public BillingBatchRunTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new BillingEngineService(_db, new EmergencyCreditGuard(_db, new StubConnectivityCommandClient()));

        _tariff = new Tariff(Guid.NewGuid(), "Flat Batch Tariff", ConsumerCategory.Domestic,
            new[] { new TariffSlab(0, null, 5m) }, fixedChargePerUnitPerMonth: 0m, prepaidEnergyRebatePercent: 0m);
        _db.Tariffs.Add(_tariff);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<List<Guid>> SeedConsumersWithDlpAsync(int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var meter = new SmartMeter(Guid.NewGuid(), $"MTR-B-{i}", MeterPhase.SinglePhase);
            var consumer = new Consumer(Guid.NewGuid(), $"ACC-B-{i}", $"Batch {i}", "Street", meter, connectedLoadKw: 1m);
            consumer.AssignTariff(_tariff.Id);
            consumer.Wallet.SetEmergencyCreditLimit(200m);
            consumer.Wallet.Credit(10000m, WalletTransactionType.Recharge, "seed");
            _db.Meters.Add(meter);
            _db.Consumers.Add(consumer);
            _db.DailyLoadProfiles.Add(new DailyLoadProfile(Guid.NewGuid(), consumer.Id, meter.Id, BillingDate,
                DateTime.UtcNow.AddHours(-2), 1000m, 1010m, DateTime.UtcNow.AddHours(-2)));
            ids.Add(consumer.Id);
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return ids;
    }

    private static readonly Func<DateTime> Cutoff = () => DateTime.UtcNow.AddHours(1);

    [Fact]
    public async Task Bills_every_consumer_across_several_batches_and_records_the_run()
    {
        await SeedConsumersWithDlpAsync(1_100); // more than two batches of 500

        var results = await _service.ProcessDailyStage1Async(BillingDate, Cutoff());

        Assert.Equal(1_100, results.Count(r => !r.Skipped));
        Assert.Equal(1_100, await _db.WalletTransactions.CountAsync(t => t.Reference != null && t.Reference.StartsWith("DLP:")));
        var run = await _db.BillingRuns.SingleAsync();
        Assert.Equal(BillingRunStatus.Completed, run.Status);
        Assert.Equal(1_100, run.ConsumerCount);
    }

    [Fact]
    public async Task A_finished_run_is_not_repeated()
    {
        await SeedConsumersWithDlpAsync(3);
        await _service.ProcessDailyStage1Async(BillingDate, Cutoff());

        var again = await _service.ProcessDailyStage1Async(BillingDate, Cutoff());

        var only = Assert.Single(again);
        Assert.True(only.Skipped);
        Assert.Contains("already ran", only.SkipReason);
        Assert.Equal(3, await _db.WalletTransactions.CountAsync(t => t.Reference != null && t.Reference.StartsWith("DLP:")));
    }

    [Fact]
    public async Task A_run_with_a_recent_heartbeat_is_left_to_its_owner()
    {
        await SeedConsumersWithDlpAsync(3);
        var live = new BillingRun(Guid.NewGuid(), BillingRun.DlpStage1RunType, BillingDate, DateTime.UtcNow);
        _db.BillingRuns.Add(live);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var results = await _service.ProcessDailyStage1Async(BillingDate, Cutoff());

        var only = Assert.Single(results);
        Assert.True(only.Skipped);
        Assert.Contains("already running", only.SkipReason);
        Assert.Equal(0, await _db.WalletTransactions.CountAsync(t => t.Reference != null && t.Reference.StartsWith("DLP:")));
    }

    [Fact]
    public async Task A_stalled_run_is_taken_over_and_resumes_after_its_cursor()
    {
        var ids = await SeedConsumersWithDlpAsync(6);
        var ordered = ids.OrderBy(i => i).ToList();

        // A previous instance committed the first three consumers (by id order) and then died an hour ago.
        var stalled = new BillingRun(Guid.NewGuid(), BillingRun.DlpStage1RunType, BillingDate, DateTime.UtcNow.AddHours(-1));
        stalled.RecordBatch(3, 0, ordered[2], DateTime.UtcNow.AddHours(-1));
        _db.BillingRuns.Add(stalled);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _service.ProcessDailyStage1Async(BillingDate, Cutoff());

        var billedConsumerIds = await _db.Consumers.Where(c => c.Wallet.Transactions.Any(t => t.Reference != null && t.Reference.StartsWith("DLP:")))
            .Select(c => c.Id).ToListAsync();
        Assert.Equal(ordered.Skip(3).OrderBy(i => i), billedConsumerIds.OrderBy(i => i));
        var run = await _db.BillingRuns.SingleAsync();
        Assert.Equal(BillingRunStatus.Completed, run.Status);
        Assert.Equal(6, run.ConsumerCount); // 3 from the dead instance + 3 resumed
    }

    private sealed class StubConnectivityCommandClient : IConnectivityCommandClient
    {
        public Task<SendConnectivityCommandResult> SendConnectivityCommandAsync(
            SendConnectivityCommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SendConnectivityCommandResult(ConnectivityCommandOutcome.Acknowledged, null));
    }
}
