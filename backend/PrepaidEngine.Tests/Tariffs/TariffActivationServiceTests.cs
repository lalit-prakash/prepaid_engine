using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Tariffs;
using Xunit;

namespace PrepaidEngine.Tests.Tariffs;

/// <summary>
/// Automatic tariff activation: due requests activate, future ones do not, historical tariff rows
/// are retired rather than edited, and one request that cannot activate never blocks the others.
/// Uses SQLite in-memory so the "one Active tariff per name" unique index is really enforced.
/// </summary>
public class TariffActivationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PrepaidEngineDbContext _db;
    private readonly TariffActivationService _service;
    private static readonly DateTime Now = new(2026, 9, 19, 10, 0, 0, DateTimeKind.Utc);

    public TariffActivationServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new TariffActivationService(_db, NullLogger<TariffActivationService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static TariffSlab[] Slabs() => new[] { new TariffSlab(0, 100, 5m), new TariffSlab(100, null, 5.5m) };

    private Tariff AddActiveTariff(string name)
    {
        var tariff = new Tariff(Guid.NewGuid(), name, ConsumerCategory.Domestic, Slabs(), 90m, 2m, 200m);
        _db.Tariffs.Add(tariff);
        _db.SaveChanges();
        return tariff;
    }

    private TariffChangeRequest AddScheduledRequest(Guid? supersedes, string name, DateTime commencement, decimal fixedCharge = 100m)
    {
        var request = new TariffChangeRequest(Guid.NewGuid(), supersedes, name, ConsumerCategory.Domestic, Slabs(), fixedCharge, 3m, 200m, "it_user", Now.AddDays(-5));
        request.Submit("it_user", "revision", Now.AddDays(-4));
        request.Approve("utility_user", commencement, Now.AddDays(-3));
        _db.TariffChangeRequests.Add(request);
        _db.SaveChanges();
        return request;
    }

    [Fact]
    public async Task ActivateDue_DueRequest_CreatesNewActiveTariffAndRetiresTheOldOne()
    {
        var old = AddActiveTariff("Domestic");
        var request = AddScheduledRequest(old.Id, "Domestic", Now.Date);

        var result = await _service.ActivateDueAsync(Now);

        Assert.Single(result.Activated);
        Assert.Empty(result.Failed);

        var tariffs = await _db.Tariffs.AsNoTracking().ToListAsync();
        Assert.Equal(TariffLifecycleStatus.Retired, tariffs.Single(t => t.Id == old.Id).Status);
        var created = tariffs.Single(t => t.Id != old.Id);
        Assert.Equal(TariffLifecycleStatus.Active, created.Status);
        Assert.Equal(100m, created.FixedChargePerUnitPerMonth);

        var refreshed = await _db.TariffChangeRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        Assert.Equal(TariffChangeRequestStatus.Activated, refreshed.Status);
        Assert.Equal(created.Id, refreshed.ResultingTariffId);

        var actions = await _db.AuditEntries.AsNoTracking().Select(a => a.Action).ToListAsync();
        Assert.Contains("ACTIVATED", actions);
        Assert.Contains("RETIRED", actions);
    }

    [Fact]
    public async Task ActivateDue_OldTariffRowIsNeverEdited()
    {
        var old = AddActiveTariff("Domestic");
        AddScheduledRequest(old.Id, "Domestic", Now.Date, fixedCharge: 150m);

        await _service.ActivateDueAsync(Now);

        var retired = await _db.Tariffs.AsNoTracking().Include(t => t.Slabs).SingleAsync(t => t.Id == old.Id);
        Assert.Equal(90m, retired.FixedChargePerUnitPerMonth);
        Assert.Equal(2, retired.Slabs.Count);
    }

    [Fact]
    public async Task ActivateDue_FutureCommencement_DoesNothing()
    {
        var old = AddActiveTariff("Domestic");
        AddScheduledRequest(old.Id, "Domestic", Now.Date.AddDays(3));

        var result = await _service.ActivateDueAsync(Now);

        Assert.Empty(result.Activated);
        Assert.Empty(result.Failed);
        Assert.Equal(TariffLifecycleStatus.Active, (await _db.Tariffs.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ActivateDue_RunTwice_ActivatesOnlyOnce()
    {
        var old = AddActiveTariff("Domestic");
        AddScheduledRequest(old.Id, "Domestic", Now.Date);

        await _service.ActivateDueAsync(Now);
        var second = await _service.ActivateDueAsync(Now);

        Assert.Empty(second.Activated);
        Assert.Equal(2, await _db.Tariffs.CountAsync());
        Assert.Equal(1, await _db.Tariffs.CountAsync(t => t.Status == TariffLifecycleStatus.Active));
    }

    [Fact]
    public async Task ActivateDue_OneRequestThatCannotActivate_DoesNotBlockTheOthers()
    {
        AddActiveTariff("Existing");
        var other = AddActiveTariff("Other");

        // Processed first (earlier commencement) and fails: a brand-new tariff whose name is already
        // held by an Active one violates the unique-Active-name index.
        var clash = AddScheduledRequest(null, "Existing", Now.Date.AddDays(-2));
        var good = AddScheduledRequest(other.Id, "Other", Now.Date);

        var result = await _service.ActivateDueAsync(Now);

        Assert.Single(result.Failed);
        Assert.Equal(clash.Id, result.Failed[0].ChangeRequestId);
        Assert.Single(result.Activated);
        Assert.Equal(good.Id, result.Activated[0].ChangeRequestId);

        _db.ChangeTracker.Clear();
        Assert.Equal(TariffChangeRequestStatus.Scheduled, (await _db.TariffChangeRequests.SingleAsync(r => r.Id == clash.Id)).Status);
        Assert.Equal(TariffChangeRequestStatus.Activated, (await _db.TariffChangeRequests.SingleAsync(r => r.Id == good.Id)).Status);
    }

    [Fact]
    public async Task ActivateDue_RequestWhoseTariffWasAlreadyRetired_IsReportedAndCreatesNothing()
    {
        var old = AddActiveTariff("Domestic");
        var first = AddScheduledRequest(old.Id, "Domestic", Now.Date.AddDays(-1), fixedCharge: 110m);
        var stale = AddScheduledRequest(old.Id, "Domestic", Now.Date, fixedCharge: 120m);

        var result = await _service.ActivateDueAsync(Now);

        Assert.Single(result.Activated);
        Assert.Equal(first.Id, result.Activated[0].ChangeRequestId);
        Assert.Single(result.Failed);
        Assert.Equal(stale.Id, result.Failed[0].ChangeRequestId);
        Assert.Equal(1, await _db.Tariffs.CountAsync(t => t.Status == TariffLifecycleStatus.Active));
    }
}
