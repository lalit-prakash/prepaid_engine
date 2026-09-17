using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrepaidEngine.Application.MeterData;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.MeterData;
using PrepaidEngine.Infrastructure.Persistence;
using Xunit;

namespace PrepaidEngine.Tests.MeterData;

/// <summary>
/// Exercises BP/LS/IP/Events/Alarms ingestion, DLP completeness, and cross-source energy
/// validation against a real (if lightweight) SQLite database — see
/// PrepaidEngineDbContextTests for why SQLite is used here rather than an in-memory fake.
/// </summary>
public class MeterDataIngestionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PrepaidEngineDbContext _db;
    private readonly MeterDataIngestionService _service;
    private readonly Consumer _consumer;
    private readonly SmartMeter _meter;

    public MeterDataIngestionServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options;
        _db = new PrepaidEngineDbContext(options);
        _db.Database.EnsureCreated();
        _service = new MeterDataIngestionService(_db, Options.Create(new EnergyValidationOptions { WarningTolerancePct = 2m, FailTolerancePct = 5m }));

        _meter = new SmartMeter(Guid.NewGuid(), "MTR-MDMS-1", MeterPhase.SinglePhase);
        _consumer = new Consumer(Guid.NewGuid(), "ACC-MDMS-1", "MDMS Test Consumer", "Test Street", _meter, connectedLoadKw: 1m);
        _db.Meters.Add(_meter);
        _db.Consumers.Add(_consumer);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // --- BP ingestion --------------------------------------------------------------------------

    [Fact]
    public async Task IngestRegisterReading_FirstTime_Received()
    {
        var result = await _service.IngestRegisterReadingAsync(new RegisterReadingRequest(
            _consumer.Id, _meter.Id, new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), 1000m));

        Assert.Equal("Received", result.Status);
        Assert.Single(_db.RegisterReadings);
    }

    [Fact]
    public async Task IngestRegisterReading_DuplicateKey_IsIdempotent()
    {
        var timestamp = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        await _service.IngestRegisterReadingAsync(new RegisterReadingRequest(_consumer.Id, _meter.Id, timestamp, 1000m));

        var second = await _service.IngestRegisterReadingAsync(new RegisterReadingRequest(_consumer.Id, _meter.Id, timestamp, 1000m));

        Assert.Equal("Duplicate", second.Status);
        Assert.Single(_db.RegisterReadings); // no second row was inserted
    }

    [Fact]
    public async Task IngestRegisterReading_NegativeCumulative_Rejected()
    {
        var result = await _service.IngestRegisterReadingAsync(new RegisterReadingRequest(
            _consumer.Id, _meter.Id, DateTime.UtcNow, -5m));

        Assert.Equal("Rejected", result.Status);
        Assert.Empty(_db.RegisterReadings);
    }

    // --- LS ingestion ----------------------------------------------------------------------------

    [Fact]
    public async Task IngestLoadSurveyInterval_DuplicateIntervalStart_IsIdempotent()
    {
        var start = new DateTime(2026, 9, 10, 6, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(30);
        await _service.IngestLoadSurveyIntervalAsync(new LoadSurveyIntervalRequest(_consumer.Id, _meter.Id, start, end, 2.5m));

        var second = await _service.IngestLoadSurveyIntervalAsync(new LoadSurveyIntervalRequest(_consumer.Id, _meter.Id, start, end, 2.5m));

        Assert.Equal("Duplicate", second.Status);
        Assert.Single(_db.LoadSurveyIntervals);
    }

    [Fact]
    public async Task IngestLoadSurveyInterval_EndBeforeStart_Rejected()
    {
        var start = DateTime.UtcNow;
        var result = await _service.IngestLoadSurveyIntervalAsync(new LoadSurveyIntervalRequest(_consumer.Id, _meter.Id, start, start.AddMinutes(-1), 1m));

        Assert.Equal("Rejected", result.Status);
    }

    // --- Events / Alarms ---------------------------------------------------------------------

    [Fact]
    public async Task IngestMeterEvent_DuplicateKey_IsIdempotent()
    {
        var at = DateTime.UtcNow;
        await _service.IngestMeterEventAsync(new MeterEventRequest(_consumer.Id, _meter.Id, MeterEventCode.PowerFailure, at));

        var second = await _service.IngestMeterEventAsync(new MeterEventRequest(_consumer.Id, _meter.Id, MeterEventCode.PowerFailure, at));

        Assert.Equal("Duplicate", second.Status);
        Assert.Single(_db.MeterEvents);
    }

    [Fact]
    public async Task MeterAlarm_AcknowledgeThenResolve_TransitionsStatus()
    {
        var result = await _service.IngestMeterAlarmAsync(new MeterAlarmRequest(
            _consumer.Id, _meter.Id, MeterAlarmCode.Tamper, MeterAlarmSeverity.Critical, DateTime.UtcNow));
        var alarm = await _db.MeterAlarms.FirstAsync(a => a.Id == result.AlarmId);

        alarm.Acknowledge("operator1", DateTime.UtcNow);
        Assert.Equal(MeterAlarmStatus.Acknowledged, alarm.Status);

        alarm.Resolve("Field visit confirmed tamper seal broken; reseated and reset.", DateTime.UtcNow);
        Assert.Equal(MeterAlarmStatus.Resolved, alarm.Status);
    }

    [Fact]
    public void MeterAlarm_ResolveWithoutNote_Throws()
    {
        var alarm = new MeterAlarm(Guid.NewGuid(), _consumer.Id, _meter.Id, MeterAlarmCode.CoverOpen, MeterAlarmSeverity.Warning, DateTime.UtcNow, DateTime.UtcNow);

        Assert.Throws<ArgumentException>(() => alarm.Resolve("", DateTime.UtcNow));
    }

    // --- DLP completeness --------------------------------------------------------------------

    [Fact]
    public async Task GetDlpCompleteness_NoDlpForDate_ReportsMissing()
    {
        var date = new DateOnly(2026, 9, 10);

        var rows = await _service.GetDlpCompletenessAsync(date);

        var row = Assert.Single(rows);
        Assert.Equal(DlpCompletenessStatus.Missing, row.Status);
    }

    [Fact]
    public async Task GetDlpCompleteness_OneValidatedDlp_ReportsComplete()
    {
        var date = new DateOnly(2026, 9, 10);
        _db.DailyLoadProfiles.Add(new DailyLoadProfile(
            Guid.NewGuid(), _consumer.Id, _meter.Id, date, DateTime.UtcNow, 100m, 142m, DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var rows = await _service.GetDlpCompletenessAsync(date);

        var row = Assert.Single(rows);
        Assert.Equal(DlpCompletenessStatus.Complete, row.Status);
    }

    // --- Energy validation (BP vs DLP, LS vs DLP) ---------------------------------------------

    [Fact]
    public async Task EvaluateEnergyValidation_BpMatchesDlpWithinTolerance_Pass()
    {
        var date = new DateOnly(2026, 9, 10);
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        _db.DailyLoadProfiles.Add(new DailyLoadProfile(Guid.NewGuid(), _consumer.Id, _meter.Id, date, DateTime.UtcNow, 1000m, 1100m, DateTime.UtcNow)); // DLP total = 100
        _db.RegisterReadings.Add(new RegisterReading(Guid.NewGuid(), _consumer.Id, _meter.Id, dayStart, 1000m, DateTime.UtcNow));
        _db.RegisterReadings.Add(new RegisterReading(Guid.NewGuid(), _consumer.Id, _meter.Id, dayStart.AddDays(1), 1101m, DateTime.UtcNow)); // BP delta = 101 (1% variance)
        await _db.SaveChangesAsync();

        var outcomes = await _service.EvaluateEnergyValidationAsync(_consumer.Id, _meter.Id, date);

        var bpVsDlp = Assert.Single(outcomes, o => o.Rule == EnergyValidationRule.BpVsDlp);
        Assert.Equal(EnergyValidationStatus.Pass, bpVsDlp.Status);
    }

    [Fact]
    public async Task EvaluateEnergyValidation_BpVsDlpLargeVariance_Fails()
    {
        var date = new DateOnly(2026, 9, 11);
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        _db.DailyLoadProfiles.Add(new DailyLoadProfile(Guid.NewGuid(), _consumer.Id, _meter.Id, date, DateTime.UtcNow, 2000m, 2100m, DateTime.UtcNow)); // DLP total = 100
        _db.RegisterReadings.Add(new RegisterReading(Guid.NewGuid(), _consumer.Id, _meter.Id, dayStart, 2000m, DateTime.UtcNow));
        _db.RegisterReadings.Add(new RegisterReading(Guid.NewGuid(), _consumer.Id, _meter.Id, dayStart.AddDays(1), 2137m, DateTime.UtcNow)); // BP delta = 137 (37% variance)
        await _db.SaveChangesAsync();

        var outcomes = await _service.EvaluateEnergyValidationAsync(_consumer.Id, _meter.Id, date);

        var bpVsDlp = Assert.Single(outcomes, o => o.Rule == EnergyValidationRule.BpVsDlp);
        Assert.Equal(EnergyValidationStatus.Fail, bpVsDlp.Status);
    }

    [Fact]
    public async Task EvaluateEnergyValidation_NoBpOrLsData_ReturnsEmpty()
    {
        var date = new DateOnly(2026, 9, 12);
        _db.DailyLoadProfiles.Add(new DailyLoadProfile(Guid.NewGuid(), _consumer.Id, _meter.Id, date, DateTime.UtcNow, 0m, 50m, DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var outcomes = await _service.EvaluateEnergyValidationAsync(_consumer.Id, _meter.Id, date);

        Assert.Empty(outcomes);
    }

    [Fact]
    public async Task EvaluateEnergyValidation_Reevaluated_UpdatesSameRowRatherThanDuplicating()
    {
        var date = new DateOnly(2026, 9, 13);
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        _db.DailyLoadProfiles.Add(new DailyLoadProfile(Guid.NewGuid(), _consumer.Id, _meter.Id, date, DateTime.UtcNow, 0m, 100m, DateTime.UtcNow));
        _db.RegisterReadings.Add(new RegisterReading(Guid.NewGuid(), _consumer.Id, _meter.Id, dayStart, 0m, DateTime.UtcNow));
        _db.RegisterReadings.Add(new RegisterReading(Guid.NewGuid(), _consumer.Id, _meter.Id, dayStart.AddDays(1), 100m, DateTime.UtcNow));
        await _db.SaveChangesAsync();

        await _service.EvaluateEnergyValidationAsync(_consumer.Id, _meter.Id, date);
        await _service.EvaluateEnergyValidationAsync(_consumer.Id, _meter.Id, date);

        Assert.Single(_db.EnergyValidationResults.Where(v => v.Rule == EnergyValidationRule.BpVsDlp));
    }

    // --- Meter-swap boundary: BP readings under a different meter must never be pulled into
    // this meter's BP delta calculation.

    [Fact]
    public async Task EvaluateEnergyValidation_BpFromDifferentMeter_NeverCompared()
    {
        var otherMeter = new SmartMeter(Guid.NewGuid(), "MTR-OTHER", MeterPhase.SinglePhase);
        _db.Meters.Add(otherMeter);

        var date = new DateOnly(2026, 9, 14);
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        _db.DailyLoadProfiles.Add(new DailyLoadProfile(Guid.NewGuid(), _consumer.Id, _meter.Id, date, DateTime.UtcNow, 0m, 50m, DateTime.UtcNow));
        // Readings recorded against a different physical meter — must not be treated as this meter's BP.
        _db.RegisterReadings.Add(new RegisterReading(Guid.NewGuid(), _consumer.Id, otherMeter.Id, dayStart, 9000m, DateTime.UtcNow));
        _db.RegisterReadings.Add(new RegisterReading(Guid.NewGuid(), _consumer.Id, otherMeter.Id, dayStart.AddDays(1), 9050m, DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var outcomes = await _service.EvaluateEnergyValidationAsync(_consumer.Id, _meter.Id, date);

        Assert.DoesNotContain(outcomes, o => o.Rule == EnergyValidationRule.BpVsDlp);
    }
}
