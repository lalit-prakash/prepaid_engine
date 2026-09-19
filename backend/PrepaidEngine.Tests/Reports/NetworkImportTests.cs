using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.Reports;

/// <summary>Loading the supply network from flat rows: matching by code, all-or-nothing saves, dry runs and consumer mapping.</summary>
public class NetworkImportTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;

    public NetworkImportTests()
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

    private static NetworkImportRow Row(string zone = "Z1", string circle = "C1", string division = "D1", string subDivision = "SD1",
        string substation = "SS1", string feeder = "F1", string dtr = "T1", string? dtrName = null)
        => new(zone, "Zone " + zone, circle, "Circle " + circle, division, "Division " + division, subDivision, "Sub " + subDivision,
            substation, "Substation " + substation, feeder, "Feeder " + feeder, dtr, dtrName ?? "DTR " + dtr);

    [Fact]
    public async Task Creates_the_whole_path_and_reuses_shared_parents()
    {
        var result = await NetworkImport.ImportHierarchyAsync(_db, new[] { Row(dtr: "T1"), Row(dtr: "T2"), Row(feeder: "F2", dtr: "T3") }, dryRun: false, "tester");

        Assert.True(result.Saved);
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Created["Zone"]);
        Assert.Equal(2, result.Created["Feeder"]);
        Assert.Equal(3, result.Created["Dtr"]);
        Assert.Equal(3, await _db.Dtrs.CountAsync());
        Assert.Equal(1, await _db.Zones.CountAsync());
        Assert.Single(await _db.AuditEntries.Where(a => a.Action == "HIERARCHY_IMPORTED").ToListAsync());
    }

    [Fact]
    public async Task Importing_the_same_file_again_changes_nothing()
    {
        var rows = new[] { Row(dtr: "T1"), Row(dtr: "T2") };
        await NetworkImport.ImportHierarchyAsync(_db, rows, false, "tester");

        var again = await NetworkImport.ImportHierarchyAsync(_db, rows, false, "tester");

        Assert.True(again.Saved);
        Assert.All(again.Created.Values, v => Assert.Equal(0, v));
        Assert.All(again.Renamed.Values, v => Assert.Equal(0, v));
        Assert.Equal(2, await _db.Dtrs.CountAsync());
    }

    [Fact]
    public async Task A_changed_name_renames_the_node_and_keeps_its_id()
    {
        await NetworkImport.ImportHierarchyAsync(_db, new[] { Row(dtr: "T1", dtrName: "Old name") }, false, "tester");
        var id = (await _db.Dtrs.SingleAsync()).Id;

        var result = await NetworkImport.ImportHierarchyAsync(_db, new[] { Row(dtr: "T1", dtrName: "New name") }, false, "tester");

        Assert.Equal(1, result.Renamed["Dtr"]);
        var dtr = await _db.Dtrs.SingleAsync();
        Assert.Equal("New name", dtr.Name);
        Assert.Equal(id, dtr.Id);
    }

    [Fact]
    public async Task A_code_under_a_different_parent_is_an_error_and_nothing_is_saved()
    {
        await NetworkImport.ImportHierarchyAsync(_db, new[] { Row(feeder: "F1", dtr: "T1") }, false, "tester");

        // DTR T1 already exists under feeder F1; this file tries to put it under F2.
        var result = await NetworkImport.ImportHierarchyAsync(_db, new[] { Row(feeder: "F2", dtr: "T1"), Row(dtr: "T9") }, false, "tester");

        Assert.False(result.Saved);
        var error = Assert.Single(result.Errors);
        Assert.Equal(1, error.Row);
        Assert.Contains("different parent", error.Message);
        Assert.Equal(1, await _db.Dtrs.CountAsync()); // T9, on the good row, was not saved either
        Assert.Equal(1, await _db.Feeders.CountAsync());
    }

    [Fact]
    public async Task One_code_with_two_names_in_a_file_is_an_error()
    {
        var result = await NetworkImport.ImportHierarchyAsync(_db,
            new[] { Row(dtr: "T1") with { ZoneName = "North" }, Row(dtr: "T2") with { ZoneName = "South" } }, false, "tester");

        Assert.False(result.Saved);
        Assert.Contains("two different names", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task Missing_values_are_reported_by_row()
    {
        var result = await NetworkImport.ImportHierarchyAsync(_db, new[] { Row(), Row() with { FeederCode = " " } }, false, "tester");

        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.Row);
        Assert.Contains("Feeder", error.Message);
        Assert.False(result.Saved);
    }

    [Fact]
    public async Task A_dry_run_reports_what_would_happen_without_saving()
    {
        var result = await NetworkImport.ImportHierarchyAsync(_db, new[] { Row(dtr: "T1"), Row(dtr: "T2") }, dryRun: true, "tester");

        Assert.False(result.Saved);
        Assert.Equal(2, result.Created["Dtr"]);
        Assert.Equal(0, await _db.Dtrs.CountAsync());
        Assert.Empty(await _db.AuditEntries.ToListAsync());
    }

    private async Task<Consumer> AddConsumerAsync(string account)
    {
        var meter = new SmartMeter(Guid.NewGuid(), "M-" + account, MeterPhase.SinglePhase);
        var consumer = new Consumer(Guid.NewGuid(), account, account, "Street", meter, 1m);
        _db.Meters.Add(meter);
        _db.Consumers.Add(consumer);
        await _db.SaveChangesAsync();
        return consumer;
    }

    [Fact]
    public async Task Maps_consumers_to_dtrs_and_counts_new_moved_and_unchanged()
    {
        await NetworkImport.ImportHierarchyAsync(_db, new[] { Row(dtr: "T1"), Row(dtr: "T2") }, false, "tester");
        await AddConsumerAsync("A1");
        await AddConsumerAsync("A2");
        await AddConsumerAsync("A3");
        await NetworkImport.MapConsumersAsync(_db, new[] { new ConsumerMappingRow("A2", "T1"), new ConsumerMappingRow("A3", "T1") }, false, "tester");

        var result = await NetworkImport.MapConsumersAsync(_db,
            new[] { new ConsumerMappingRow("A1", "T1"), new ConsumerMappingRow("A2", "T2"), new ConsumerMappingRow("A3", "T1") }, false, "tester");

        Assert.True(result.Saved);
        Assert.Equal(1, result.ConsumersMapped);
        Assert.Equal(1, result.ConsumersRemapped);
        Assert.Equal(1, result.ConsumersUnchanged);
        var t2 = await _db.Dtrs.SingleAsync(d => d.Code == "T2");
        Assert.Equal(t2.Id, (await _db.Consumers.SingleAsync(c => c.AccountNumber == "A2")).DtrId);
    }

    [Fact]
    public async Task Mapping_reports_unknown_accounts_unknown_dtrs_and_duplicates_and_saves_nothing()
    {
        await NetworkImport.ImportHierarchyAsync(_db, new[] { Row(dtr: "T1") }, false, "tester");
        await AddConsumerAsync("A1");

        var result = await NetworkImport.MapConsumersAsync(_db, new[]
        {
            new ConsumerMappingRow("A1", "T1"),      // fine
            new ConsumerMappingRow("NOPE", "T1"),    // unknown account
            new ConsumerMappingRow("A1", "T9"),      // duplicate account (and unknown DTR)
        }, false, "tester");

        Assert.False(result.Saved);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("No consumer", result.Errors[0].Message);
        Assert.Contains("more than once", result.Errors[1].Message);
        Assert.Null((await _db.Consumers.SingleAsync()).DtrId);
    }
}
