using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Persistence;
using Xunit;

namespace PrepaidEngine.Tests.Billing;

/// <summary>
/// Exercises the DLP billing pipeline's core financial rules (daily charge, provisional billing,
/// emergency-credit auto-disconnect, meter replacement) against a real (if lightweight) SQLite
/// database — see PrepaidEngineDbContextTests for why SQLite is used here rather than an
/// in-memory fake.
/// </summary>
public class BillingEngineServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PrepaidEngineDbContext _db;
    private readonly BillingEngineService _service;
    private readonly Consumer _consumer;
    private readonly Tariff _tariff;

    public BillingEngineServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options;
        _db = new PrepaidEngineDbContext(options);
        _db.Database.EnsureCreated();
        _service = new BillingEngineService(_db, new EmergencyCreditGuard(_db, new StubConnectivityCommandClient()));

        // ₹5/kWh flat slab, no rebate, ₹0 fixed charge — keeps the worked-example math simple.
        _tariff = new Tariff(Guid.NewGuid(), "Flat Test Tariff", ConsumerCategory.Domestic,
            new[] { new TariffSlab(0, null, 5m) }, fixedChargePerUnitPerMonth: 0m, prepaidEnergyRebatePercent: 0m);
        _db.Tariffs.Add(_tariff);

        var meter = new SmartMeter(Guid.NewGuid(), "MTR-DLP-1", MeterPhase.SinglePhase);
        _consumer = new Consumer(Guid.NewGuid(), "ACC-DLP-1", "DLP Test Consumer", "Test Street", meter, connectedLoadKw: 1m);
        _consumer.AssignTariff(_tariff.Id);
        _consumer.Wallet.SetEmergencyCreditLimit(200m);
        _consumer.Wallet.Credit(10000m, WalletTransactionType.Recharge, "seed");
        _db.Meters.Add(meter);
        _db.Consumers.Add(_consumer);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Consumer> ReloadConsumerAsync()
    {
        using var fresh = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        return await fresh.Consumers.Include(c => c.Wallet).ThenInclude(w => w.Transactions).SingleAsync(c => c.Id == _consumer.Id);
    }

    // ------------------------------------------------------------------ Daily processing

    [Fact]
    public async Task ProcessDaily_RealDlp_DebitsWalletDirectly()
    {
        var billingDate = new DateOnly(2026, 9, 11);
        var dayStart = billingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // 24 kWh @ ₹5/kWh = ₹120, charged in one direct debit.
        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(
            _consumer.Id, _consumer.Meter.Id, billingDate, dayStart.AddDays(1).AddSeconds(15), 0m, 24m));

        var results = await _service.ProcessDailyAsync(billingDate);

        Assert.Single(results);
        Assert.True(results[0].DlpAvailable);
        Assert.False(results[0].IsProvisional);
        Assert.Equal(120m, results[0].ChargeAmount);

        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m - 120m, reloaded.Wallet.Balance);
        Assert.Single(reloaded.Wallet.Transactions.Where(t => t.Type == WalletTransactionType.DailyDlpCharge));
    }

    [Fact]
    public async Task ProcessDaily_MeterOnBillingHold_SkipsConsumer()
    {
        _db.MeterBillingControls.Add(new MeterBillingControl(Guid.NewGuid(), _consumer.Id, _consumer.Meter.Id, "test hold", DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var billingDate = new DateOnly(2026, 9, 11);
        var results = await _service.ProcessDailyAsync(billingDate);

        Assert.True(results[0].Skipped);
        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m, reloaded.Wallet.Balance);
    }

    [Fact]
    public async Task ProcessDaily_MissingDlp_CreatesProvisionalAndDebitsEstimate()
    {
        var billingDate = new DateOnly(2026, 9, 11);

        var results = await _service.ProcessDailyAsync(billingDate);

        Assert.True(results[0].IsProvisional);
        Assert.Equal(0m, results[0].ChargeAmount); // no history to estimate from -> 0 kWh estimate

        var profile = await _db.DailyLoadProfiles.SingleAsync(d => d.ConsumerId == _consumer.Id && d.ProfileDate == billingDate);
        Assert.True(profile.IsProvisional);
    }

    [Fact]
    public async Task ProcessDaily_SameDateTwice_SecondRunIsSkipped()
    {
        var billingDate = new DateOnly(2026, 9, 11);

        await _service.ProcessDailyAsync(billingDate);
        var second = await _service.ProcessDailyAsync(billingDate);

        Assert.True(second[0].Skipped);
        Assert.Single(await _db.BillingRuns.Where(r => r.BillingDate == billingDate).ToListAsync());
    }

    [Fact]
    public async Task ProcessDaily_ChargeCrossesEmergencyCredit_AutoDisconnects()
    {
        // Wallet starts near empty so a modest daily charge pushes it below the -200 emergency
        // credit limit — the guard should auto-dispatch a Disconnect command.
        var reset = await _db.Consumers.Include(c => c.Wallet).ThenInclude(w => w.Transactions).SingleAsync(c => c.Id == _consumer.Id);
        var drainTransaction = reset.Wallet.Debit(9900m, WalletTransactionType.Adjustment, "drain-to-100");
        _db.WalletTransactions.Add(drainTransaction);
        await _db.SaveChangesAsync();

        var billingDate = new DateOnly(2026, 9, 11);
        var dayStart = billingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // 100 kWh @ ₹5/kWh = ₹500 — balance goes from ₹100 to -₹400, past the ₹200 emergency
        // credit limit.
        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(
            _consumer.Id, _consumer.Meter.Id, billingDate, dayStart.AddDays(1).AddSeconds(15), 0m, 100m));

        await _service.ProcessDailyAsync(billingDate);

        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(ConnectionStatus.Disconnected, reloaded.ConnectionStatus);

        var command = await _db.ConnectivityCommands.SingleAsync(c => c.ConsumerId == _consumer.Id);
        Assert.Equal(ConnectivityCommandType.Disconnect, command.CommandType);
        Assert.Equal(ConnectivityCommandStatus.Acknowledged, command.Status);
        Assert.Equal(EmergencyCreditGuard.AutoDisconnectReason, command.Reason);
    }

    // ------------------------------------------------------------------ Meter replacement

    [Fact]
    public async Task ReplaceMeter_SwapsMeterAndRecordsAssignment_WithoutComparingReadings()
    {
        // Captured before the call: ReplaceMeterAsync mutates this same tracked Consumer
        // instance's Meter navigation in place, so _consumer.Meter.Id would reflect the NEW
        // meter by the time of the assertions below otherwise.
        var originalMeterId = _consumer.Meter.Id;

        var assignment = await _service.ReplaceMeterAsync(_consumer.Id, new MeterReplacementRequest(
            "MTR-NEW-1", MeterPhase.SinglePhase, DateTime.UtcNow, OldMeterClosingReadingKwh: 12450.25m,
            NewMeterOpeningReadingKwh: 25.10m, Reason: "Defective meter replacement"));

        Assert.Equal(originalMeterId, assignment.OldMeterId);
        Assert.Equal(12450.25m, assignment.OldMeterClosingReadingKwh);
        Assert.Equal(25.10m, assignment.NewMeterOpeningReadingKwh);

        var reloadedConsumer = await _db.Consumers.Include(c => c.Meter).SingleAsync(c => c.Id == _consumer.Id);
        Assert.Equal("MTR-NEW-1", reloadedConsumer.Meter.MeterNumber);
        Assert.NotEqual(assignment.OldMeterId, reloadedConsumer.Meter.Id);
    }

    /// <summary>Always acknowledges — a minimal stand-in so
    /// <see cref="EmergencyCreditGuard"/> can be exercised here without pulling in the full mock
    /// simulator's marker-string conventions this test file has no need of.</summary>
    private class StubConnectivityCommandClient : IConnectivityCommandClient
    {
        public Task<SendConnectivityCommandResult> SendConnectivityCommandAsync(
            SendConnectivityCommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SendConnectivityCommandResult(ConnectivityCommandOutcome.Acknowledged, null));
    }
}
