using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Persistence.Seed;

namespace PrepaidEngine.Tests.Tariffs;

public class TariffCatalogueSeedTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;

    public TariffCatalogueSeedTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    [Fact]
    public async Task Seeding_an_empty_database_gives_all_nineteen_schedules_matching_the_book()
    {
        await TariffCatalogue2026.SeedAsync(_db);
        var active = await _db.Tariffs.Include(t => t.Slabs).Include(t => t.TouPeriods).Where(t => t.Status == TariffLifecycleStatus.Active).ToListAsync();
        Assert.Equal(19, TariffBookCheck.Run(active).Count(r => r.Status == "Matches"));
    }

    [Fact]
    public async Task A_seeded_tariff_that_differs_from_the_book_is_replaced_and_its_consumers_move()
    {
        var old = new Tariff(Guid.NewGuid(), "MePDCL Domestic (DLT)", ConsumerCategory.Domestic, new[] { new TariffSlab(0, 100, 5.70m), new TariffSlab(100, null, 5.90m) }, 105m, 3m);
        _db.Tariffs.Add(old);
        var meter = new SmartMeter(Guid.NewGuid(), "M-1", MeterPhase.SinglePhase);
        var c = new Consumer(Guid.NewGuid(), "ACC-1", "Name", "Street", meter, 2m);
        c.AssignTariff(old.Id);
        _db.Meters.Add(meter);
        _db.Consumers.Add(c);
        await _db.SaveChangesAsync();

        await TariffCatalogue2026.SeedAsync(_db);

        var moved = await _db.Consumers.AsNoTracking().SingleAsync();
        var current = await _db.Tariffs.Include(t => t.Slabs).AsNoTracking().SingleAsync(t => t.Id == moved.TariffId);
        Assert.NotEqual(old.Id, current.Id);
        Assert.Equal(TariffLifecycleStatus.Active, current.Status);
        Assert.Equal(90m, current.FixedChargePerUnitPerMonth);
        Assert.Equal(3, current.Slabs.Count);
        Assert.Equal(TariffLifecycleStatus.Retired, (await _db.Tariffs.AsNoTracking().SingleAsync(t => t.Id == old.Id)).Status);
    }
}
