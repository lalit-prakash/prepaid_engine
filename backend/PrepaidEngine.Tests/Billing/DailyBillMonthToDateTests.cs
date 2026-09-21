using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.Billing;

/// <summary>The daily run prices each day's slabs from the month-to-date consumption and records the bill's components.</summary>
public class DailyBillMonthToDateTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private readonly BillingEngineService _service;
    private readonly Consumer _consumer;
    private readonly Tariff _tariff;

    public DailyBillMonthToDateTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new BillingEngineService(_db, new EmergencyCreditGuard(_db, new StubConnectivityCommandClient()));

        // The domestic slabs (0-100 @ 5.00, 100-200 @ 5.04, 200+ @ 5.10), no rebate and no fixed charge, so only the slab and duty arithmetic shows.
        _tariff = new Tariff(Guid.NewGuid(), "Slab Test Tariff", ConsumerCategory.Domestic,
            new[] { new TariffSlab(0, 100, 5.00m), new TariffSlab(100, 200, 5.04m), new TariffSlab(200, null, 5.10m) },
            fixedChargePerUnitPerMonth: 0m, prepaidEnergyRebatePercent: 0m);
        _db.Tariffs.Add(_tariff);
        var meter = new SmartMeter(Guid.NewGuid(), "MTR-MTD-1", MeterPhase.SinglePhase);
        _consumer = new Consumer(Guid.NewGuid(), "ACC-MTD-1", "Month To Date", "Test Street", meter, connectedLoadKw: 1m);
        _consumer.AssignTariff(_tariff.Id);
        _consumer.Wallet.SetEmergencyCreditLimit(200m);
        _consumer.Wallet.Credit(10_000m, WalletTransactionType.Recharge, "seed");
        _db.Meters.Add(meter);
        _db.Consumers.Add(_consumer);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<decimal> BillDayAsync(DateOnly day, decimal startReading, decimal endReading)
    {
        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(_consumer.Id, _consumer.Meter.Id, day, DateTime.UtcNow, startReading, endReading));
        var results = await _service.ProcessDailyStage1Async(day, DateTime.UtcNow.AddHours(1));
        return results.Single().ChargeAmount;
    }

    [Fact]
    public async Task A_day_that_crosses_a_slab_boundary_is_priced_part_in_each_slab()
    {
        // Day 1: 90 kWh, all in the first slab: 450.00 + 4.50 duty.
        Assert.Equal(454.50m, await BillDayAsync(new DateOnly(2026, 9, 10), 0m, 90m));

        // Day 2: 12 kWh with 90 already billed this month: 10 x 5.00 + 2 x 5.04 = 60.08, plus 0.60 duty. Restarting the slabs would give 60.60.
        Assert.Equal(60.68m, await BillDayAsync(new DateOnly(2026, 9, 11), 90m, 102m));
    }

    [Fact]
    public async Task Each_day_is_recorded_with_its_components_and_the_month_to_date_it_started_from()
    {
        await BillDayAsync(new DateOnly(2026, 9, 10), 0m, 90m);
        await BillDayAsync(new DateOnly(2026, 9, 11), 90m, 102m);

        var bills = (await _db.DailyBills.AsNoTracking().OrderBy(b => b.BillDate).ToListAsync());
        Assert.Equal(2, bills.Count);
        Assert.Equal((0m, 90m, 450.00m, 4.50m, 454.50m), (bills[0].MonthToDateKwhBefore, bills[0].Kwh, bills[0].GrossEnergyCharge, bills[0].ElectricityDuty, bills[0].Total));
        Assert.Equal((90m, 12m, 60.08m, 0.60m, 60.68m), (bills[1].MonthToDateKwhBefore, bills[1].Kwh, bills[1].GrossEnergyCharge, bills[1].ElectricityDuty, bills[1].Total));
        Assert.All(bills, b => Assert.False(b.IsProvisional));
        Assert.All(bills, b => Assert.Equal(_tariff.Id, b.TariffId));
    }

    [Fact]
    public async Task The_wallet_is_debited_exactly_the_recorded_total()
    {
        await BillDayAsync(new DateOnly(2026, 9, 10), 0m, 90m);
        await BillDayAsync(new DateOnly(2026, 9, 11), 90m, 102m);

        using var fresh = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        var wallet = (await fresh.Consumers.Include(c => c.Wallet).SingleAsync(c => c.Id == _consumer.Id)).Wallet;
        var billed = await fresh.DailyBills.SumAsync(b => (double)b.Total);
        Assert.Equal(10_000m - 454.50m - 60.68m, wallet.Balance);
        Assert.Equal(515.18, billed, 2);
    }

    [Fact]
    public async Task The_slabs_start_again_on_the_first_of_the_next_month()
    {
        await BillDayAsync(new DateOnly(2026, 9, 30), 0m, 150m);

        // 1 October: the month-to-date is back to zero, so 10 kWh is priced at the first slab (50.00 + 0.50 duty), not the second.
        Assert.Equal(50.50m, await BillDayAsync(new DateOnly(2026, 10, 1), 150m, 160m));
    }

    [Fact]
    public async Task Running_the_same_day_twice_does_not_bill_it_twice()
    {
        var day = new DateOnly(2026, 9, 10);
        await BillDayAsync(day, 0m, 90m);
        await _service.ProcessDailyStage1Async(day, DateTime.UtcNow.AddHours(1));
        Assert.Single(await _db.DailyBills.ToListAsync());
    }

    private sealed class StubConnectivityCommandClient : IConnectivityCommandClient
    {
        public Task<SendConnectivityCommandResult> SendConnectivityCommandAsync(SendConnectivityCommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SendConnectivityCommandResult(ConnectivityCommandOutcome.Acknowledged, null));
    }
}
