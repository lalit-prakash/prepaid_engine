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
/// Exercises the two-stage DLP billing pipeline's core financial rules (Stage 1/Stage 2 receipt-
/// time splitting, provisional billing, negative-consumption validation on ingest, emergency-
/// credit auto-disconnect, meter replacement) against a real (if lightweight) SQLite database —
/// see PrepaidEngineDbContextTests for why SQLite is used here rather than an in-memory fake.
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

    // ------------------------------------------------------------------ Ingest validation

    [Fact]
    public async Task Ingest_ValidBlock_MarksReceived()
    {
        var day = new DateOnly(2026, 9, 10);
        var result = await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day, DateTime.UtcNow, 1000m, 1024m));

        Assert.Equal("Validated", result.Status);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task Ingest_StartBelowPreviousDaysClosingReading_RejectsAndActivatesBillingHold()
    {
        var day1 = new DateOnly(2026, 9, 10);
        var day2 = new DateOnly(2026, 9, 11);

        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day1, DateTime.UtcNow, 1000m, 1024m));

        // Day 2's starting reading (1010) is lower than day 1's closing reading (1024) — a
        // negative-consumption sequence across the day boundary.
        var result = await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day2, DateTime.UtcNow, 1010m, 1030m));

        Assert.Equal("Rejected", result.Status);
        Assert.Contains("Negative consumption", result.Message);

        var control = await _db.MeterBillingControls.SingleAsync(c => c.MeterId == _consumer.Meter.Id);
        Assert.True(control.ActualBillingBlocked);
    }

    [Fact]
    public async Task Ingest_StartAtOrAbovePreviousDaysClosingReading_Accepted()
    {
        var day1 = new DateOnly(2026, 9, 10);
        var day2 = new DateOnly(2026, 9, 11);

        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day1, DateTime.UtcNow, 1000m, 1024m));

        var result = await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day2, DateTime.UtcNow, 1024m, 1050m));

        Assert.Equal("Validated", result.Status);
        Assert.Empty(await _db.MeterBillingControls.ToListAsync());
    }

    // ------------------------------------------------------------------ Stage 1 / Stage 2 split

    [Fact]
    public async Task Stage1_DlpReceivedBeforeCutoff_BillsDirectly()
    {
        var billingDate = new DateOnly(2026, 9, 11);

        // 24 kWh @ ₹5/kWh = ₹120 energy charge, plus ₹1.20 electricity duty (24 units × ₹0.05, Domestic) = ₹121.20.
        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, billingDate, DateTime.UtcNow, 0m, 24m));

        var cutoff = DateTime.UtcNow.AddHours(1); // ingest already happened before this cutoff
        var results = await _service.ProcessDailyStage1Async(billingDate, cutoff);

        Assert.Single(results);
        Assert.Equal("Stage1", results[0].Stage);
        Assert.Equal(121.20m, results[0].ChargeAmount);

        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m - 121.20m, reloaded.Wallet.Balance);
    }

    [Fact]
    public async Task Stage1_DlpReceivedAfterCutoff_IsNotBilled()
    {
        var billingDate = new DateOnly(2026, 9, 11);

        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, billingDate, DateTime.UtcNow, 0m, 24m));

        var cutoffBeforeIngest = DateTime.UtcNow.AddHours(-1); // ingest happened after this cutoff
        var results = await _service.ProcessDailyStage1Async(billingDate, cutoffBeforeIngest);

        Assert.Empty(results); // nothing to bill yet in Stage 1

        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m, reloaded.Wallet.Balance);
    }

    [Fact]
    public async Task Stage2_DlpMissedStage1Cutoff_BillsInStage2()
    {
        var billingDate = new DateOnly(2026, 9, 11);

        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, billingDate, DateTime.UtcNow, 0m, 24m));

        // Stage 1 misses it (cutoff before ingest)...
        await _service.ProcessDailyStage1Async(billingDate, DateTime.UtcNow.AddHours(-1));
        // ...Stage 2 catches it (cutoff after ingest).
        var results = await _service.ProcessDailyStage2Async(billingDate, DateTime.UtcNow.AddHours(1));

        Assert.Single(results);
        Assert.Equal("Stage2", results[0].Stage);
        Assert.Equal(121.20m, results[0].ChargeAmount);

        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m - 121.20m, reloaded.Wallet.Balance);
    }

    [Fact]
    public async Task Stage2_NoDlpByCutoff_CreatesProvisionalCharge()
    {
        var billingDate = new DateOnly(2026, 9, 11);

        var results = await _service.ProcessDailyStage2Async(billingDate, DateTime.UtcNow);

        Assert.Single(results);
        Assert.Equal("Stage2Provisional", results[0].Stage);
        Assert.True(results[0].IsProvisional);
        Assert.Equal(0m, results[0].ChargeAmount); // no history to estimate from -> 0 kWh estimate

        var profile = await _db.DailyLoadProfiles.SingleAsync(d => d.ConsumerId == _consumer.Id && d.ProfileDate == billingDate);
        Assert.True(profile.IsProvisional);
    }

    [Fact]
    public async Task Stage2_RejectedDlpWithHoldSinceCleared_DoesNotCollideOnProvisionalCreate()
    {
        // Reproduces a real bug found in review: a Rejected DLP already occupies the
        // Consumer+Meter+ProfileDate slot (unique-indexed). If its MeterBillingControl hold is
        // cleared before a corrected DLP is re-ingested, Stage 2 must not try to create a second
        // (provisional) DailyLoadProfile row for that same slot — that would violate the unique
        // index and crash the whole batch's SaveChangesAsync.
        var day1 = new DateOnly(2026, 9, 10);
        var billingDate = new DateOnly(2026, 9, 11);

        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day1, DateTime.UtcNow, 1000m, 1024m));
        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, billingDate, DateTime.UtcNow, 1010m, 1030m)); // rejected: 1010 < 1024

        var control = await _db.MeterBillingControls.SingleAsync(c => c.MeterId == _consumer.Meter.Id);
        control.Clear(DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var results = await _service.ProcessDailyStage2Async(billingDate, DateTime.UtcNow);

        Assert.True(results[0].Skipped);
        Assert.Contains("rejected", results[0].SkipReason, StringComparison.OrdinalIgnoreCase);
        // Only the one (Rejected) DLP row exists for this date — no colliding second row was created.
        Assert.Single(await _db.DailyLoadProfiles.Where(d => d.ProfileDate == billingDate).ToListAsync());
    }

    [Fact]
    public async Task Ingest_PreviousDayWasRejected_DoesNotUseItAsBaseline()
    {
        // A Rejected profile's readings are untrustworthy — using one as the "previous day"
        // baseline could produce a false rejection (or false acceptance) for the next day.
        var day1 = new DateOnly(2026, 9, 9);
        var day2 = new DateOnly(2026, 9, 10);
        var day3 = new DateOnly(2026, 9, 11);

        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day1, DateTime.UtcNow, 1000m, 1024m));
        // day2 rejected (its End of 1030 is untrustworthy - should never be used as a baseline).
        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day2, DateTime.UtcNow, 1010m, 1030m));

        // day3's start (1024) matches day1's real closing reading exactly — must be accepted,
        // not compared against day2's (rejected, untrustworthy) 1030 closing reading.
        var result = await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day3, DateTime.UtcNow, 1024m, 1048m));

        Assert.Equal("Validated", result.Status);
    }

    [Fact]
    public async Task Stage2_MeterOnBillingHold_SkipsConsumer()
    {
        _db.MeterBillingControls.Add(new MeterBillingControl(Guid.NewGuid(), _consumer.Id, _consumer.Meter.Id, "test hold", DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var billingDate = new DateOnly(2026, 9, 11);
        var results = await _service.ProcessDailyStage2Async(billingDate, DateTime.UtcNow);

        Assert.True(results[0].Skipped);
        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m, reloaded.Wallet.Balance);
    }

    [Fact]
    public async Task Stage1_SameDateTwice_SecondRunIsSkipped()
    {
        var billingDate = new DateOnly(2026, 9, 11);

        await _service.ProcessDailyStage1Async(billingDate, DateTime.UtcNow);
        var second = await _service.ProcessDailyStage1Async(billingDate, DateTime.UtcNow);

        Assert.True(second[0].Skipped);
        Assert.Single(await _db.BillingRuns.Where(r => r.RunType == BillingRun.DlpStage1RunType && r.BillingDate == billingDate).ToListAsync());
    }

    [Fact]
    public async Task Stage2_SameDateTwice_SecondRunIsSkipped()
    {
        var billingDate = new DateOnly(2026, 9, 11);

        await _service.ProcessDailyStage2Async(billingDate, DateTime.UtcNow);
        var second = await _service.ProcessDailyStage2Async(billingDate, DateTime.UtcNow);

        Assert.True(second[0].Skipped);
        Assert.Single(await _db.BillingRuns.Where(r => r.RunType == BillingRun.DlpStage2RunType && r.BillingDate == billingDate).ToListAsync());
    }

    [Fact]
    public async Task Stage2_ChargeCrossesEmergencyCredit_AutoDisconnects()
    {
        // Wallet starts near empty so a modest daily charge pushes it below the -200 emergency
        // credit limit — the guard should auto-dispatch a Disconnect command.
        var reset = await _db.Consumers.Include(c => c.Wallet).ThenInclude(w => w.Transactions).SingleAsync(c => c.Id == _consumer.Id);
        var drainTransaction = reset.Wallet.Debit(9900m, WalletTransactionType.Adjustment, "drain-to-100");
        _db.WalletTransactions.Add(drainTransaction);
        await _db.SaveChangesAsync();

        var billingDate = new DateOnly(2026, 9, 11);

        // 100 kWh @ ₹5/kWh = ₹500 — balance goes from ₹100 to -₹400, past the ₹200 emergency
        // credit limit.
        await _service.IngestDailyLoadProfileAsync(
            new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, billingDate, DateTime.UtcNow, 0m, 100m));

        await _service.ProcessDailyStage1Async(billingDate, DateTime.UtcNow.AddHours(1));

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
