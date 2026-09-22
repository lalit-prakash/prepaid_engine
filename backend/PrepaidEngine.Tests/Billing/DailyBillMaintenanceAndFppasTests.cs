using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Persistence.Seed;
using Xunit;

namespace PrepaidEngine.Tests.Billing;

/// <summary>The daily run charges an HT/EHT consumer's own opted-in TMC/CPMC and the LT-side metering surcharge from their
/// recorded billing facts, and their share of a notified FPPAS rate from their own prior month's energy charge.</summary>
public class DailyBillMaintenanceAndFppasTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 9, 21);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private readonly BillingEngineService _service;

    public DailyBillMaintenanceAndFppasTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new BillingEngineService(_db, new EmergencyCreditGuard(_db, new AckClient()));
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private sealed class AckClient : IConnectivityCommandClient
    {
        public Task<SendConnectivityCommandResult> SendConnectivityCommandAsync(SendConnectivityCommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SendConnectivityCommandResult(ConnectivityCommandOutcome.Acknowledged, null));
    }

    private Consumer AddHtConsumer()
    {
        var entry = TariffCatalogue2026.Build().Single(e => e.ScheduleCode == "CHT");
        var tariff = TariffCatalogue2026.ToTariff(entry);
        _db.Tariffs.Add(tariff);
        var meter = new SmartMeter(Guid.NewGuid(), "M-CHT", MeterPhase.ThreePhase);
        var c = new Consumer(Guid.NewGuid(), "ACC-CHT", "CHT", "Street", meter, 60m);
        c.AssignTariff(tariff.Id);
        c.Wallet.SetEmergencyCreditLimit(2000m);
        c.Wallet.Credit(1_000_000m, WalletTransactionType.Recharge, "seed");
        _db.Meters.Add(meter);
        _db.Consumers.Add(c);
        _db.SaveChanges();
        return c;
    }

    [Fact]
    public async Task TMC_CPMC_and_the_LT_side_surcharge_are_billed_from_the_consumers_own_facts()
    {
        var c = AddHtConsumer();
        c.SetBillingFacts(SupplyVoltage.Kv11, meteredOnLtSide: true, transformerMaintenanceOptedIn: true, transformerCapacityKva: 100m,
            ctPtMaintenanceOptedIn: true, CtPtWiring.ThreePhaseThreeWire);
        _db.SaveChanges();

        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(c.Id, c.Meter.Id, Day, DateTime.UtcNow, 0m, 100m, null, 0m, 125m));
        await _service.ProcessDailyStage1Async(Day, DateTime.UtcNow.AddHours(1));
        var bill = await _db.DailyBills.AsNoTracking().SingleAsync(b => b.ConsumerId == c.Id);

        // TMC: Rs.20/kVA/month at 11kV x 100 kVA = Rs.2,000/month; CPMC: Rs.800/month (11kV, 3-wire) — both prorated to the day at x12/365.
        Assert.Equal(2000m * 12m / 365m, bill.Tmc, 6);
        Assert.Equal(800m * 12m / 365m, bill.Cpmc, 6);
        Assert.Equal(bill.GrossEnergyCharge * PrepaidEngine.Domain.TariffBookParameters.LtSideMeteringSurchargeRate, bill.LtSideMeteringSurcharge, 6);
    }

    [Fact]
    public async Task Not_opting_in_bills_no_maintenance_charge_even_when_the_supply_voltage_is_recorded()
    {
        var c = AddHtConsumer();
        c.SetBillingFacts(SupplyVoltage.Kv11, meteredOnLtSide: false, false, null, false, null);
        _db.SaveChanges();

        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(c.Id, c.Meter.Id, Day, DateTime.UtcNow, 0m, 100m, null, 0m, 125m));
        await _service.ProcessDailyStage1Async(Day, DateTime.UtcNow.AddHours(1));
        var bill = await _db.DailyBills.AsNoTracking().SingleAsync(b => b.ConsumerId == c.Id);

        Assert.Equal((0m, 0m, 0m), (bill.Tmc, bill.Cpmc, bill.LtSideMeteringSurcharge));
    }

    [Fact]
    public async Task An_LT_consumer_with_no_billing_facts_is_billed_no_maintenance_charge_or_surcharge()
    {
        var entry = TariffCatalogue2026.Build().Single(e => e.ScheduleCode == "DLT");
        var tariff = TariffCatalogue2026.ToTariff(entry);
        _db.Tariffs.Add(tariff);
        var meter = new SmartMeter(Guid.NewGuid(), "M-DLT", MeterPhase.SinglePhase);
        var c = new Consumer(Guid.NewGuid(), "ACC-DLT", "DLT", "Street", meter, 2m);
        c.AssignTariff(tariff.Id);
        c.Wallet.Credit(10_000m, WalletTransactionType.Recharge, "seed");
        _db.Meters.Add(meter);
        _db.Consumers.Add(c);
        _db.SaveChanges();

        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(c.Id, c.Meter.Id, Day, DateTime.UtcNow, 0m, 10m, null));
        await _service.ProcessDailyStage1Async(Day, DateTime.UtcNow.AddHours(1));
        var bill = await _db.DailyBills.AsNoTracking().SingleAsync(b => b.ConsumerId == c.Id);

        Assert.Equal((0m, 0m, 0m), (bill.Tmc, bill.Cpmc, bill.LtSideMeteringSurcharge));
    }

    [Fact]
    public async Task A_consumers_FPPAS_share_is_their_own_prior_month_energy_charge_times_the_notified_rate()
    {
        var c = AddHtConsumer();
        _db.SaveChanges();

        // Give the consumer a known prior-month (August) energy charge of Rs.6,000, split across two days for realism.
        var tariff = await _db.Tariffs.SingleAsync(t => t.Id == c.TariffId);
        DailyBillBreakdown Breakdown(decimal gross) => DailyBillCalculator.Calculate(new DailyBillInput(tariff, 60m, 0m, 0m)) with { GrossEnergyCharge = gross };
        _db.DailyBills.Add(new DailyBill(Guid.NewGuid(), c.Id, tariff.Id, new DateOnly(2026, 8, 10), "PRIOR-1", isProvisional: false, Breakdown(4000m), DateTime.UtcNow));
        _db.DailyBills.Add(new DailyBill(Guid.NewGuid(), c.Id, tariff.Id, new DateOnly(2026, 8, 20), "PRIOR-2", isProvisional: false, Breakdown(2000m), DateTime.UtcNow));
        // A September bill (the run's own month) must not be counted as "prior month".
        _db.DailyBills.Add(new DailyBill(Guid.NewGuid(), c.Id, tariff.Id, new DateOnly(2026, 9, 1), "THIS-MONTH", isProvisional: false, Breakdown(999m), DateTime.UtcNow));
        _db.FppasRateNotifications.Add(new FppasRateNotification(Guid.NewGuid(), -0.14m, new DateTime(2026, 8, 15)));
        _db.SaveChanges();

        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(c.Id, c.Meter.Id, Day, DateTime.UtcNow, 0m, 0m, null, 0m, 0m));
        await _service.ProcessDailyStage1Async(Day, DateTime.UtcNow.AddHours(1));
        var bill = await _db.DailyBills.AsNoTracking().SingleAsync(b => b.ConsumerId == c.Id && b.BillDate == Day);

        // Rs.6,000 x -14% = -Rs.840, spread over September's 30 days, rounded to the cent = -Rs.28.00/day exactly.
        Assert.Equal(-28.00m, bill.FppasShare);
    }

    [Fact]
    public async Task No_FPPAS_is_charged_when_no_rate_was_notified_for_the_billing_month()
    {
        var c = AddHtConsumer();
        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(c.Id, c.Meter.Id, Day, DateTime.UtcNow, 0m, 0m, null, 0m, 0m));
        await _service.ProcessDailyStage1Async(Day, DateTime.UtcNow.AddHours(1));
        var bill = await _db.DailyBills.AsNoTracking().SingleAsync(b => b.ConsumerId == c.Id);

        Assert.Equal(0m, bill.FppasShare);
    }
}
