using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Persistence;
using Xunit;

namespace PrepaidEngine.Tests.Billing;

/// <summary>
/// Exercises the LS/DLP billing pipeline's core financial rules (hourly wallet debits, negative-
/// consumption hold, daily settlement, provisional billing, meter replacement) against a real
/// (if lightweight) SQLite database — see PrepaidEngineDbContextTests for why SQLite is used here
/// rather than an in-memory fake.
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
        _service = new BillingEngineService(_db);

        // ₹5/kWh flat slab, no rebate, ₹0 fixed charge — keeps the worked-example math simple.
        _tariff = new Tariff(Guid.NewGuid(), "Flat Test Tariff", ConsumerCategory.Domestic,
            new[] { new TariffSlab(0, null, 5m) }, fixedChargePerUnitPerMonth: 0m, prepaidEnergyRebatePercent: 0m);
        _db.Tariffs.Add(_tariff);

        var meter = new SmartMeter(Guid.NewGuid(), "MTR-LS-1", MeterPhase.SinglePhase);
        _consumer = new Consumer(Guid.NewGuid(), "ACC-LS-1", "LS Test Consumer", "Test Street", meter, connectedLoadKw: 1m);
        _consumer.AssignTariff(_tariff.Id);
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

    // ------------------------------------------------------------------ LS ingestion

    [Fact]
    public async Task IngestLoadSurvey_ValidBlock_MarksValidated()
    {
        var hourStart = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var results = await _service.IngestLoadSurveyAsync(new[]
        {
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, hourStart, hourStart.AddMinutes(30), 10m, 10m),
        });

        Assert.Single(results);
        Assert.Equal("Valid", results[0].Quality);
        Assert.Equal("Validated", results[0].Status);
    }

    [Fact]
    public async Task IngestLoadSurvey_DuplicateBlock_IsRejected()
    {
        var hourStart = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var block = new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, hourStart, hourStart.AddMinutes(30), 10m, 10m);

        await _service.IngestLoadSurveyAsync(new[] { block });
        var results = await _service.IngestLoadSurveyAsync(new[] { block });

        Assert.Equal("Duplicate", results[0].Quality);
        Assert.Equal("Rejected", results[0].Status);
    }

    [Fact]
    public async Task IngestLoadSurvey_NegativeConsumption_RejectsAndActivatesBillingHold()
    {
        var hourStart = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        await _service.IngestLoadSurveyAsync(new[]
        {
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, hourStart, hourStart.AddMinutes(30), 100m, 10m),
        });

        // Next block's cumulative reading is LOWER than the previous one — a real negative-
        // consumption sequence, not zero.
        var results = await _service.IngestLoadSurveyAsync(new[]
        {
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, hourStart.AddMinutes(30), hourStart.AddMinutes(60), 50m, 0m),
        });

        Assert.Equal("NegativeConsumption", results[0].Quality);
        Assert.Equal("Rejected", results[0].Status);

        var control = await _db.MeterBillingControls.SingleAsync(c => c.MeterId == _consumer.Meter.Id);
        Assert.True(control.ActualBillingBlocked);
    }

    // ------------------------------------------------------------------ Hourly processing

    [Fact]
    public async Task ProcessCompletedHour_TwoValidBlocks_DebitsWalletOnce()
    {
        var hourEnd = new DateTime(2026, 9, 11, 11, 0, 0, DateTimeKind.Utc);
        await _service.IngestLoadSurveyAsync(new[]
        {
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, hourEnd.AddMinutes(-60), hourEnd.AddMinutes(-30), 10m, 10m),
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, hourEnd.AddMinutes(-30), hourEnd, 15m, 5m),
        });

        var results = await _service.ProcessCompletedHourAsync(hourEnd);

        Assert.Single(results);
        Assert.Equal(2, results[0].BlocksProcessed);
        Assert.Equal(75m, results[0].TotalCharge); // (10+5) kWh * ₹5/kWh

        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m - 75m, reloaded.Wallet.Balance);
    }

    [Fact]
    public async Task ProcessCompletedHour_RunTwice_DoesNotDoubleDebit()
    {
        var hourEnd = new DateTime(2026, 9, 11, 11, 0, 0, DateTimeKind.Utc);
        await _service.IngestLoadSurveyAsync(new[]
        {
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, hourEnd.AddMinutes(-60), hourEnd.AddMinutes(-30), 10m, 10m),
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, hourEnd.AddMinutes(-30), hourEnd, 15m, 5m),
        });

        await _service.ProcessCompletedHourAsync(hourEnd);
        await _service.ProcessCompletedHourAsync(hourEnd);

        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m - 75m, reloaded.Wallet.Balance);
        // One debit per block (2 blocks) — re-running the hour must not add a 3rd/4th.
        Assert.Equal(2, reloaded.Wallet.Transactions.Count(t => t.Type == WalletTransactionType.LoadSurveyHourlyCharge));
    }

    [Fact]
    public async Task ProcessCompletedHour_MeterOnBillingHold_SkipsConsumer()
    {
        _db.MeterBillingControls.Add(new MeterBillingControl(Guid.NewGuid(), _consumer.Id, _consumer.Meter.Id, "test hold", DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var hourEnd = new DateTime(2026, 9, 11, 11, 0, 0, DateTimeKind.Utc);
        await _service.IngestLoadSurveyAsync(new[]
        {
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, hourEnd.AddMinutes(-30), hourEnd, 10m, 10m),
        });

        var results = await _service.ProcessCompletedHourAsync(hourEnd);

        Assert.True(results[0].Skipped);
        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m, reloaded.Wallet.Balance);
    }

    // ------------------------------------------------------------------ Daily settlement (spec §31 worked example)

    [Fact]
    public async Task ProcessDaily_AuthoritativeChargeExceedsLsDebits_PostsPositiveSettlement()
    {
        var billingDate = new DateOnly(2026, 9, 11);
        var dayStart = billingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // 21.6 kWh of hourly LS debits already posted @ ₹5/kWh = ₹108.
        await _service.IngestLoadSurveyAsync(new[]
        {
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, dayStart, dayStart.AddMinutes(30), 10.8m, 10.8m),
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, dayStart.AddMinutes(30), dayStart.AddHours(1), 21.6m, 10.8m),
        });
        await _service.ProcessCompletedHourAsync(dayStart.AddHours(1));

        // Authoritative DLP: 24 kWh @ ₹5/kWh = ₹120.
        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(
            _consumer.Id, _consumer.Meter.Id, billingDate, dayStart.AddDays(1).AddSeconds(15), 0m, 24m));

        var results = await _service.ProcessDailyAsync(billingDate);

        Assert.Single(results);
        Assert.Equal(120m, results[0].AuthoritativeCharge);
        Assert.Equal(108m, results[0].LsDebitTotal);
        Assert.Equal(12m, results[0].Settlement);

        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m - 108m - 12m, reloaded.Wallet.Balance);
    }

    [Fact]
    public async Task ProcessDaily_LsDebitsExceedAuthoritativeCharge_PostsCreditAdjustment()
    {
        var billingDate = new DateOnly(2026, 9, 11);
        var dayStart = billingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // 25 kWh of LS debits @ ₹5/kWh = ₹125.
        await _service.IngestLoadSurveyAsync(new[]
        {
            new LoadSurveyBlockRequest(_consumer.Id, _consumer.Meter.Id, dayStart, dayStart.AddMinutes(30), 25m, 25m),
        });
        await _service.ProcessCompletedHourAsync(dayStart.AddMinutes(30));

        // Authoritative DLP: 24 kWh @ ₹5/kWh = ₹120.
        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(
            _consumer.Id, _consumer.Meter.Id, billingDate, dayStart.AddDays(1).AddSeconds(15), 0m, 24m));

        var results = await _service.ProcessDailyAsync(billingDate);

        Assert.Equal(-5m, results[0].Settlement);

        var reloaded = await ReloadConsumerAsync();
        Assert.Equal(10000m - 125m + 5m, reloaded.Wallet.Balance);
    }

    [Fact]
    public async Task ProcessDaily_MissingDlp_CreatesProvisionalAndDebitsEstimate()
    {
        var billingDate = new DateOnly(2026, 9, 11);

        var results = await _service.ProcessDailyAsync(billingDate);

        Assert.True(results[0].IsProvisional);
        Assert.Equal(0m, results[0].AuthoritativeCharge); // no history to estimate from -> 0 kWh estimate

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
}
