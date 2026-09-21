using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Persistence.Seed;

namespace PrepaidEngine.Tests.Billing;

/// <summary>The daily run bills a kVAh tariff on the profile's kVAh and a Time-of-Day tariff by band from the load survey intervals.</summary>
public class DailyBillKvahAndTimeOfDayTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 9, 21);
    // 00:00 IST on 21 Sep is 18:30 UTC on 20 Sep.
    private static readonly DateTime DayStartUtc = new(2026, 9, 20, 18, 30, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private readonly BillingEngineService _service;

    public DailyBillKvahAndTimeOfDayTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new BillingEngineService(_db, new EmergencyCreditGuard(_db, new AckClient()));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class AckClient : IConnectivityCommandClient
    {
        public Task<SendConnectivityCommandResult> SendConnectivityCommandAsync(SendConnectivityCommandRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SendConnectivityCommandResult(ConnectivityCommandOutcome.Acknowledged, null));
    }

    private Consumer AddConsumer(string scheduleCode, decimal loadKva)
    {
        var tariff = TariffCatalogue2026.ToTariff(TariffCatalogue2026.Build().Single(e => e.ScheduleCode == scheduleCode));
        _db.Tariffs.Add(tariff);
        var meter = new SmartMeter(Guid.NewGuid(), "M-" + scheduleCode, MeterPhase.ThreePhase);
        var c = new Consumer(Guid.NewGuid(), "ACC-" + scheduleCode, scheduleCode, "Street", meter, loadKva);
        c.AssignTariff(tariff.Id);
        c.Wallet.SetEmergencyCreditLimit(200m);
        c.Wallet.Credit(1_000_000m, WalletTransactionType.Recharge, "seed");
        _db.Meters.Add(meter);
        _db.Consumers.Add(c);
        _db.SaveChanges();
        return c;
    }

    private async Task<DailyBill> BillAsync(Consumer c, decimal kwh, decimal? kvah)
    {
        await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(c.Id, c.Meter.Id, Day, DateTime.UtcNow, 0m, kwh, null, kvah.HasValue ? 0m : null, kvah));
        await _service.ProcessDailyStage1Async(Day, DateTime.UtcNow.AddHours(1));
        return await _db.DailyBills.AsNoTracking().SingleAsync(b => b.ConsumerId == c.Id);
    }

    private void AddInterval(Consumer c, int minutesFromIstMidnight, decimal kwh, decimal? kvah)
    {
        var start = DayStartUtc.AddMinutes(minutesFromIstMidnight);
        _db.LoadSurveyIntervals.Add(new LoadSurveyInterval(Guid.NewGuid(), c.Id, c.Meter.Id, start, start.AddMinutes(30), kwh, DateTime.UtcNow, null, kvah));
        _db.SaveChanges();
    }

    [Fact]
    public async Task A_kVAh_tariff_is_billed_on_the_profiles_kVAh()
    {
        var c = AddConsumer("CHT", 60m);                    // 6.00 per kVAh, fixed 390 per kVA
        var bill = await BillAsync(c, kwh: 100m, kvah: 125m);

        Assert.Equal(("kVAh", 125m, 750.00m), (bill.BilledUnit, bill.BilledEnergy, bill.GrossEnergyCharge));
        Assert.Equal(100m, bill.Kwh);
        Assert.Equal(6.00m, bill.ElectricityDuty);          // duty is on the 100 kWh units
        Assert.Null(bill.Notes);
    }

    [Fact]
    public async Task A_kVAh_tariff_on_a_profile_without_kVAh_falls_back_to_kWh_and_the_bill_says_so()
    {
        var c = AddConsumer("CHT", 60m);
        var bill = await BillAsync(c, kwh: 100m, kvah: null);

        Assert.Equal(("kWh", 600.00m), (bill.BilledUnit, bill.GrossEnergyCharge));
        Assert.Contains("no kVAh reading", bill.Notes);
    }

    [Fact]
    public async Task A_Time_of_Day_tariff_is_billed_by_band_from_the_load_survey()
    {
        var c = AddConsumer("IHT", 100m);
        AddInterval(c, 7 * 60, 540m, 600m);      // 07:00 IST: Normal
        AddInterval(c, 18 * 60, 270m, 300m);     // 18:00 IST: Peak
        AddInterval(c, 1 * 60, 90m, 100m);       // 01:00 IST: Off-Peak
        var bill = await BillAsync(c, kwh: 900m, kvah: 1000m);

        Assert.Equal(600m * 5.55m + 300m * 6.66m + 100m * 4.72m, bill.GrossEnergyCharge);   // 5,800
        Assert.Equal("kVAh", bill.BilledUnit);
        Assert.Null(bill.Notes);
    }

    [Fact]
    public async Task Time_of_Day_energy_with_no_load_survey_is_priced_at_the_Normal_rate_and_says_so()
    {
        var c = AddConsumer("IHT", 100m);
        var bill = await BillAsync(c, kwh: 900m, kvah: 1000m);

        Assert.Equal(1000m * 5.55m, bill.GrossEnergyCharge);
        Assert.Contains("no load survey intervals", bill.Notes);
    }

    [Fact]
    public async Task Intervals_from_another_day_are_not_used()
    {
        var c = AddConsumer("IHT", 100m);
        AddInterval(c, -60, 900m, 1000m);        // 23:00 IST the day before
        var bill = await BillAsync(c, kwh: 900m, kvah: 1000m);
        Assert.Contains("no load survey intervals", bill.Notes);
    }

    [Fact]
    public async Task The_minimum_demand_applies_to_an_HT_consumers_fixed_charge()
    {
        var c = AddConsumer("CHT", 40m);                    // below 56 kVA
        var bill = await BillAsync(c, kwh: 0m, kvah: 0m);
        Assert.Equal(390m * 56m * 12m / 365m, bill.FixedCharge, 6);
    }

    [Fact]
    public async Task A_profile_with_only_one_of_the_two_kVAh_readings_is_rejected()
    {
        var c = AddConsumer("CHT", 60m);
        var result = await _service.IngestDailyLoadProfileAsync(new DailyLoadProfileRequest(c.Id, c.Meter.Id, Day, DateTime.UtcNow, 0m, 100m, null, 0m, null));
        Assert.Equal("Rejected", result.Status);
    }
}
