using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;
using Xunit;

namespace PrepaidEngine.Tests.Persistence;

/// <summary>
/// Sanity-checks the EF Core model against a real (if lightweight) relational engine.
/// SQL Server is the configured production provider (see appsettings.json); SQLite is used
/// here purely so the mapping — keys, FKs, owned collections, backing-field navigations —
/// can be exercised without requiring a SQL Server instance in the dev/CI environment.
/// </summary>
public class PrepaidEngineDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PrepaidEngineDbContext _context;

    public PrepaidEngineDbContextTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PrepaidEngineDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new PrepaidEngineDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void CanPersistAndReloadConsumerWithMeterAndWallet()
    {
        var meter = new SmartMeter(Guid.NewGuid(), "MTR-100");
        var consumer = new Consumer(Guid.NewGuid(), "ACC-100", "Jane Doe", "1 Test Street", meter);
        consumer.Wallet.Credit(250m, WalletTransactionType.Recharge, "RMS-1");

        _context.Meters.Add(meter);
        _context.Consumers.Add(consumer);
        _context.SaveChanges();

        using var freshContext = new PrepaidEngineDbContext(
            new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);

        var reloaded = freshContext.Consumers
            .Include(c => c.Meter)
            .Include(c => c.Wallet).ThenInclude(w => w.Transactions)
            .Single(c => c.Id == consumer.Id);

        Assert.Equal("ACC-100", reloaded.AccountNumber);
        Assert.Equal("MTR-100", reloaded.Meter.MeterNumber);
        Assert.Equal(250m, reloaded.Wallet.Balance);
        Assert.Single(reloaded.Wallet.Transactions);
    }

    [Fact]
    public void CanPersistAndReloadTariffWithSlabs()
    {
        var tariff = new Tariff(Guid.NewGuid(), "Domestic", new[]
        {
            new TariffSlab(0, 100, 5.00m),
            new TariffSlab(100, null, 7.50m)
        });

        _context.Tariffs.Add(tariff);
        _context.SaveChanges();

        using var freshContext = new PrepaidEngineDbContext(
            new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);

        var reloaded = freshContext.Tariffs.Include(t => t.Slabs).Single(t => t.Id == tariff.Id);

        Assert.Equal(2, reloaded.Slabs.Count);
        Assert.Equal(875.00m, reloaded.CalculateAmount(150));
    }

    [Fact]
    public void RmsReferenceId_IsUniqueAcrossRechargeTransactions()
    {
        var meter = new SmartMeter(Guid.NewGuid(), "MTR-200");
        var consumer = new Consumer(Guid.NewGuid(), "ACC-200", "John Roe", "2 Test Street", meter);
        _context.Consumers.Add(consumer);
        _context.SaveChanges();

        _context.RechargeTransactions.Add(
            new RechargeTransaction(Guid.NewGuid(), consumer.Id, 100m, "RMS-DUP-1", DateTime.UtcNow));
        _context.SaveChanges();

        _context.RechargeTransactions.Add(
            new RechargeTransaction(Guid.NewGuid(), consumer.Id, 200m, "RMS-DUP-1", DateTime.UtcNow));

        Assert.Throws<DbUpdateException>(() => _context.SaveChanges());
    }
}
