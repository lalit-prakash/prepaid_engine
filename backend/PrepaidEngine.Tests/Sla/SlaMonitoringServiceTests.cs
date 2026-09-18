using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Sla;
using Xunit;

namespace PrepaidEngine.Tests.Sla;

/// <summary>Exercises real SLA computation against a lightweight SQLite database — see
/// PrepaidEngineDbContextTests for why SQLite is used here rather than an in-memory fake.</summary>
public class SlaMonitoringServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PrepaidEngineDbContext _db;
    private readonly SlaMonitoringService _service;

    public SlaMonitoringServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options;
        _db = new PrepaidEngineDbContext(options);
        _db.Database.EnsureCreated();
        _service = new SlaMonitoringService(_db, Options.Create(new SlaMonitoringOptions
        {
            DlpIngestionTargetMinutes = 120m,
            BillingRunTargetMinutes = 30m,
            RechargeCompletionTargetMinutes = 5m,
            MeterCreditTargetMinutes = 5m,
            ConnectivityCommandTargetMinutes = 10m,
        }));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetSlaSummary_NoData_ReportsUnavailableForEveryMetric()
    {
        var metrics = await _service.GetSlaSummaryAsync();

        Assert.All(metrics, m => Assert.Equal("Unavailable", m.Status));
        Assert.All(metrics, m => Assert.Equal(0, m.SampleSize));
    }

    private (Guid ConsumerId, Guid MeterId) SeedConsumer(string accountNumber)
    {
        var meter = new SmartMeter(Guid.NewGuid(), $"MTR-{accountNumber}", MeterPhase.SinglePhase);
        var consumer = new Consumer(Guid.NewGuid(), accountNumber, "SLA Test Consumer", "Test Street", meter, connectedLoadKw: 1m);
        _db.Meters.Add(meter);
        _db.Consumers.Add(consumer);
        _db.SaveChanges();
        return (consumer.Id, meter.Id);
    }

    [Fact]
    public async Task GetSlaSummary_DlpWithinTarget_ReportsMet()
    {
        var (consumerId, meterId) = SeedConsumer("ACC-SLA-1");
        var generatedAt = new DateTime(2026, 9, 10, 0, 5, 0, DateTimeKind.Utc);
        var receivedAt = generatedAt.AddMinutes(30); // well within the 120-minute target
        _db.DailyLoadProfiles.Add(new DailyLoadProfile(Guid.NewGuid(), consumerId, meterId, new DateOnly(2026, 9, 9), generatedAt, 0m, 10m, receivedAt));
        await _db.SaveChangesAsync();

        var metrics = await _service.GetSlaSummaryAsync();

        var dlpMetric = Assert.Single(metrics, m => m.Name == "DLP Ingestion");
        Assert.Equal("Met", dlpMetric.Status);
        Assert.Equal(1, dlpMetric.SampleSize);
        Assert.Equal(0, dlpMetric.BreachCount);
    }

    [Fact]
    public async Task GetSlaSummary_DlpBeyondTarget_ReportsBreach()
    {
        var (consumerId, meterId) = SeedConsumer("ACC-SLA-2");
        var generatedAt = new DateTime(2026, 9, 10, 0, 5, 0, DateTimeKind.Utc);
        var receivedAt = generatedAt.AddMinutes(300); // well beyond the 120-minute target
        _db.DailyLoadProfiles.Add(new DailyLoadProfile(Guid.NewGuid(), consumerId, meterId, new DateOnly(2026, 9, 9), generatedAt, 0m, 10m, receivedAt));
        await _db.SaveChangesAsync();

        var metrics = await _service.GetSlaSummaryAsync();

        var dlpMetric = Assert.Single(metrics, m => m.Name == "DLP Ingestion");
        Assert.Equal("Breached", dlpMetric.Status);
        Assert.Equal(1, dlpMetric.BreachCount);
    }

    [Fact]
    public async Task GetSlaSummary_ProvisionalDlp_ExcludedFromSample()
    {
        var (consumerId, meterId) = SeedConsumer("ACC-SLA-3");
        _db.DailyLoadProfiles.Add(DailyLoadProfile.CreateProvisional(Guid.NewGuid(), consumerId, meterId, new DateOnly(2026, 9, 9), DateTime.UtcNow, 12m));
        await _db.SaveChangesAsync();

        var metrics = await _service.GetSlaSummaryAsync();

        var dlpMetric = Assert.Single(metrics, m => m.Name == "DLP Ingestion");
        Assert.Equal(0, dlpMetric.SampleSize);
    }

    [Fact]
    public async Task GetSlaSummary_MeterCreditAcknowledged_ComputesAverage()
    {
        var (consumerId, _) = SeedConsumer("ACC-SLA-4");
        var recharge = new RechargeTransaction(Guid.NewGuid(), consumerId, 300m, "RMS-SLA-1", DateTime.UtcNow.AddMinutes(-10));
        _db.RechargeTransactions.Add(recharge);
        await _db.SaveChangesAsync();

        var command = new MeterCommand(Guid.NewGuid(), consumerId, recharge.Id, 100m, DateTime.UtcNow.AddMinutes(-10));
        command.MarkSent(DateTime.UtcNow.AddMinutes(-8));
        command.MarkAcknowledged(DateTime.UtcNow.AddMinutes(-7));
        _db.MeterCommands.Add(command);
        await _db.SaveChangesAsync();

        var metrics = await _service.GetSlaSummaryAsync();

        var meterCreditMetric = Assert.Single(metrics, m => m.Name == "Meter Credit Acknowledgement");
        Assert.Equal(1, meterCreditMetric.SampleSize);
        Assert.NotNull(meterCreditMetric.ActualAverageMinutes);
    }
}
