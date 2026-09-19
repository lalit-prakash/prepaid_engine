using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Persistence.Seed;

namespace PrepaidEngine.Tests.Reports;

/// <summary>The network hierarchy filter and grouping used by every report, against a real (SQLite) database.</summary>
public class HierarchyTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private readonly Dtr _dtr1, _dtr2, _dtr3;
    private readonly Feeder _feederA, _feederB;
    private readonly Zone _zone;

    public HierarchyTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _zone = new Zone(Guid.NewGuid(), "Z", "North Zone");
        var circle = new Circle(Guid.NewGuid(), _zone.Id, "C", "North Circle");
        var division = new Division(Guid.NewGuid(), circle.Id, "D", "North Division");
        var subDivision = new SubDivision(Guid.NewGuid(), division.Id, "SD", "North Sub-division");
        var substation = new Substation(Guid.NewGuid(), subDivision.Id, "SS", "North Substation");
        _feederA = new Feeder(Guid.NewGuid(), substation.Id, "FA", "Feeder A");
        _feederB = new Feeder(Guid.NewGuid(), substation.Id, "FB", "Feeder B");
        _dtr1 = new Dtr(Guid.NewGuid(), _feederA.Id, "T1", "DTR 1");
        _dtr2 = new Dtr(Guid.NewGuid(), _feederA.Id, "T2", "DTR 2");
        _dtr3 = new Dtr(Guid.NewGuid(), _feederB.Id, "T3", "DTR 3");
        _db.AddRange(_zone, circle, division, subDivision, substation, _feederA, _feederB, _dtr1, _dtr2, _dtr3);

        AddConsumer("ACC-1", _dtr1);
        AddConsumer("ACC-2", _dtr2);
        AddConsumer("ACC-3", _dtr3);
        AddConsumer("ACC-4", null); // not mapped to any DTR
        _db.SaveChanges();
    }

    private void AddConsumer(string account, Dtr? dtr)
    {
        var meter = new SmartMeter(Guid.NewGuid(), "M-" + account, MeterPhase.SinglePhase);
        var consumer = new Consumer(Guid.NewGuid(), account, account, "Street", meter, 1m);
        if (dtr is not null) consumer.AssignDtr(dtr.Id);
        _db.Meters.Add(meter);
        _db.Consumers.Add(consumer);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static HierarchyQuery Filter(Guid? zone = null, Guid? feeder = null, Guid? dtr = null)
        => new(zone, null, null, null, null, feeder, dtr);

    private async Task<List<string>> AccountsAsync(HierarchyQuery h)
        => await _db.Consumers.ByHierarchy(h).OrderBy(c => c.AccountNumber).Select(c => c.AccountNumber).ToListAsync();

    [Fact]
    public async Task No_filter_keeps_every_consumer_including_unmapped_ones()
        => Assert.Equal(new[] { "ACC-1", "ACC-2", "ACC-3", "ACC-4" }, await AccountsAsync(Filter()));

    [Fact]
    public async Task Filtering_by_a_feeder_keeps_only_consumers_under_it()
    {
        Assert.Equal(new[] { "ACC-1", "ACC-2" }, await AccountsAsync(Filter(feeder: _feederA.Id)));
        Assert.Equal(new[] { "ACC-3" }, await AccountsAsync(Filter(feeder: _feederB.Id)));
    }

    [Fact]
    public async Task Filtering_by_a_zone_drops_unmapped_consumers()
        => Assert.Equal(new[] { "ACC-1", "ACC-2", "ACC-3" }, await AccountsAsync(Filter(zone: _zone.Id)));

    [Fact]
    public async Task Filtering_by_a_dtr_keeps_one_consumer()
        => Assert.Equal(new[] { "ACC-2" }, await AccountsAsync(Filter(dtr: _dtr2.Id)));

    [Fact]
    public async Task Filters_combine_so_a_mismatched_pair_matches_nothing()
        => Assert.Empty(await AccountsAsync(Filter(feeder: _feederB.Id, dtr: _dtr1.Id)));

    [Theory]
    [InlineData("zone", "North Zone")]
    [InlineData("feeder", "Feeder A")]
    [InlineData("dtr", "DTR 1")]
    public async Task KeyFor_names_the_consumers_node_at_the_chosen_level(string level, string expected)
    {
        var keys = await _db.Consumers.Where(c => c.AccountNumber == "ACC-1").Select(Hierarchy.KeyFor(level)).ToListAsync();
        Assert.Equal(expected, Assert.Single(keys).Key);
    }

    [Fact]
    public async Task KeyFor_is_null_for_an_unmapped_consumer_and_when_no_level_is_asked()
    {
        var unmapped = await _db.Consumers.Where(c => c.AccountNumber == "ACC-4").Select(Hierarchy.KeyFor("zone")).SingleAsync();
        Assert.Null(unmapped.Key);
        var ungrouped = await _db.Consumers.Where(c => c.AccountNumber == "ACC-1").Select(Hierarchy.KeyFor(null)).SingleAsync();
        Assert.Null(ungrouped.Key);
    }

    [Theory]
    [InlineData("zone", true)]
    [InlineData("SubDivision", true)]
    [InlineData("region", false)]
    [InlineData(null, false)]
    public void IsLevel_accepts_only_the_seven_levels(string? level, bool expected)
        => Assert.Equal(expected, Hierarchy.IsLevel(level));

    [Fact]
    public async Task Demo_network_maps_every_unmapped_consumer_and_is_idempotent()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        for (var i = 0; i < 6; i++)
        {
            var meter = new SmartMeter(Guid.NewGuid(), "DM-" + i, MeterPhase.SinglePhase);
            db.Meters.Add(meter);
            db.Consumers.Add(new Consumer(Guid.NewGuid(), "DEMO-" + i, "Demo " + i, "Street", meter, 1m));
        }
        await db.SaveChangesAsync();

        await DbSeeder.SeedDemoNetworkAsync(db);
        Assert.Equal(0, await db.Consumers.CountAsync(c => c.DtrId == null));
        Assert.Equal(1, await db.Zones.CountAsync());

        await DbSeeder.SeedDemoNetworkAsync(db);
        Assert.Equal(1, await db.Zones.CountAsync());
    }

    [Fact]
    public async Task Demo_network_is_not_forced_onto_a_database_that_already_has_its_own_network()
    {
        await DbSeeder.SeedDemoNetworkAsync(_db); // this database already holds a real (non-demo) zone
        Assert.Equal(1, await _db.Consumers.CountAsync(c => c.DtrId == null));
        Assert.Equal(1, await _db.Zones.CountAsync());
    }
}
