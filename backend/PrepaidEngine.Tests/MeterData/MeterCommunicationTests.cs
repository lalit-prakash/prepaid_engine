using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.MeterData;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.MeterData;

public class MeterCommunicationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly PrepaidEngineDbContext _db;

    public MeterCommunicationTests()
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

    private async Task<SmartMeter> AddMeterAsync()
    {
        var meter = new SmartMeter(Guid.NewGuid(), "M-" + Guid.NewGuid().ToString("N")[..8], MeterPhase.SinglePhase);
        _db.Meters.Add(meter);
        await _db.SaveChangesAsync();
        return meter;
    }

    private async Task<DateTime?> LastAsync(Guid id) => await _db.Meters.AsNoTracking().Where(m => m.Id == id).Select(m => m.LastCommunicatedAt).SingleAsync();

    [Fact]
    public async Task A_meter_that_never_sent_anything_has_no_communication_time()
    {
        var meter = await AddMeterAsync();
        Assert.Null(await LastAsync(meter.Id));
    }

    [Fact]
    public async Task Touch_sets_the_time_the_first_time()
    {
        var meter = await AddMeterAsync();
        var at = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        await MeterCommunication.TouchAsync(_db, meter.Id, at);
        Assert.Equal(at, await LastAsync(meter.Id));
    }

    [Fact]
    public async Task Touch_only_moves_the_time_forward()
    {
        var meter = await AddMeterAsync();
        var later = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        var earlier = later.AddHours(-5);
        await MeterCommunication.TouchAsync(_db, meter.Id, later);
        await MeterCommunication.TouchAsync(_db, meter.Id, earlier); // a late, out-of-order message
        Assert.Equal(later, await LastAsync(meter.Id));

        var newest = later.AddHours(2);
        await MeterCommunication.TouchAsync(_db, meter.Id, newest);
        Assert.Equal(newest, await LastAsync(meter.Id));
    }

    [Fact]
    public async Task Touch_for_an_unknown_meter_changes_nothing_and_does_not_fail()
    {
        var meter = await AddMeterAsync();
        await MeterCommunication.TouchAsync(_db, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Null(await LastAsync(meter.Id));
    }
}
