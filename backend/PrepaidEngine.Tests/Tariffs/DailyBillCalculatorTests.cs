using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence.Seed;

namespace PrepaidEngine.Tests.Tariffs;

/// <summary>The daily prepaid bill, worked by hand against the FY 2026-27 tariff book.</summary>
public class DailyBillCalculatorTests
{
    private static Tariff Schedule(string code) => TariffCatalogue2026.ToTariff(TariffCatalogue2026.Build().Single(e => e.ScheduleCode == code));

    private const decimal DailyFixedPerKwDlt = 90m * 12m / 365m; // 2.958904...

    [Fact]
    public void A_day_inside_the_first_domestic_slab()
    {
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("DLT"), ConnectedLoadOrContractDemand: 1m, DayKwh: 30m));
        Assert.Equal(150.00m, b.GrossEnergyCharge);          // 30 x 5.00
        Assert.Equal(3.00m, b.PrepaidRebate);                // 2% of gross
        Assert.Equal(147.00m, b.NetEnergyCharge);
        Assert.Equal(DailyFixedPerKwDlt, b.FixedCharge);
        Assert.Equal(1.50m, b.ElectricityDuty);              // 30 x 0.05
        Assert.Equal(Math.Round(147m + DailyFixedPerKwDlt + 1.5m, 2), b.TotalRounded);   // 151.46
    }

    [Fact]
    public void The_slabs_continue_across_days_instead_of_restarting()
    {
        // The month-to-date is 90 kWh; today's 12 kWh crosses 100: 10 at 5.00 and 2 at 5.04 = 60.08 (verified against MePDCL's reference workbook).
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("DLT"), 1m, DayKwh: 12m, MonthToDateKwhBefore: 90m));
        Assert.Equal(60.08m, b.GrossEnergyCharge);

        // Restarting the slabs each day would have priced all 12 at 5.00 = 60.00, under-charging.
        var restarted = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("DLT"), 1m, DayKwh: 12m, MonthToDateKwhBefore: 0m));
        Assert.Equal(60.00m, restarted.GrossEnergyCharge);
    }

    [Fact]
    public void A_day_deep_in_the_top_slab_is_priced_at_the_top_rate()
    {
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("DLT"), 1m, DayKwh: 10m, MonthToDateKwhBefore: 250m));
        Assert.Equal(51.00m, b.GrossEnergyCharge); // 10 x 5.10
    }

    [Fact]
    public void The_fixed_charge_accrues_on_a_day_with_no_consumption()
    {
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("DLT"), 2m, DayKwh: 0m, MonthToDateKwhBefore: 150m));
        Assert.Equal(0m, b.GrossEnergyCharge);
        Assert.Equal(2m * DailyFixedPerKwDlt, b.FixedCharge);
        Assert.Equal(0m, b.ElectricityDuty);
    }

    [Fact]
    public void An_HT_consumer_is_never_billed_fixed_charge_on_less_than_56_kva()
    {
        var dht = Schedule("DHT");
        Assert.Equal(TariffBookParameters.HtMinimumChargeableKva, dht.MinimumChargeableDemand);
        var below = DailyBillCalculator.Calculate(new DailyBillInput(dht, ConnectedLoadOrContractDemand: 40m, DayKwh: 0m));
        var atFloor = DailyBillCalculator.Calculate(new DailyBillInput(dht, 56m, 0m));
        var above = DailyBillCalculator.Calculate(new DailyBillInput(dht, 100m, 0m));
        Assert.Equal(atFloor.FixedCharge, below.FixedCharge);
        Assert.Equal(350m * 56m * 12m / 365m, below.FixedCharge);
        Assert.Equal(350m * 100m * 12m / 365m, above.FixedCharge);
    }

    [Fact]
    public void Low_tension_has_no_minimum_demand()
    {
        Assert.Null(Schedule("DLT").MinimumChargeableDemand);
        Assert.Null(Schedule("IEHT").MinimumChargeableDemand);
    }

    [Fact]
    public void Industrial_duty_tiers_run_across_the_month()
    {
        // 14,990 units so far, 20 today: 10 at 0.05 and 10 at 0.045.
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("ILT"), 10m, DayKwh: 20m, MonthToDateKwhBefore: 14_990m));
        Assert.Equal(0.95m, b.ElectricityDuty);
    }

    [Theory]
    [InlineData("CLT", 0.06)]   // "Others"
    [InlineData("GP", 0.06)]
    [InlineData("DHT", 0.05)]   // Domestic, whatever the voltage
    [InlineData("KJM", 0.05)]   // Kutir Jyoti / BPL
    [InlineData("PL", 0.06)]
    public void Duty_follows_the_category(string code, double perUnit)
    {
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule(code), 5m, DayKwh: 100m));
        Assert.Equal(100m * (decimal)perUnit, b.ElectricityDuty);
    }

    [Fact]
    public void A_ToD_tariff_without_band_data_is_priced_at_the_Normal_rate_and_says_so()
    {
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("IHT"), 100m, DayKwh: 1000m));
        Assert.Equal(5550m, b.GrossEnergyCharge);           // 1000 x 5.55
        Assert.Contains(b.Notes, n => n.Contains("Time-of-Day") && n.Contains("Normal") && n.Contains("no load survey"));
    }

    [Fact]
    public void A_ToD_tariff_with_band_data_is_priced_by_band()
    {
        var bands = new Dictionary<string, decimal> { ["Normal"] = 600m, ["Peak"] = 300m, ["Off-Peak"] = 100m };
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("IHT"), 100m, DayKwh: 1000m, TouKvahByBand: bands));
        Assert.Equal(600m * 5.55m + 300m * 6.66m + 100m * 4.72m, b.GrossEnergyCharge); // 3330 + 1998 + 472
        Assert.DoesNotContain(b.Notes, n => n.Contains("Normal rate"));
        Assert.Contains(b.Notes, n => n.Contains("kVAh") && n.Contains("kWh was used"));  // this call gave no kVAh reading
    }

    [Fact]
    public void Metering_on_the_LT_side_adds_three_percent_of_the_energy_charge()
    {
        var without = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("CHT"), 100m, DayKwh: 500m));
        var with = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("CHT"), 100m, DayKwh: 500m, MeteredOnLtSide: true));
        Assert.Equal(0m, without.LtSideMeteringSurcharge);
        Assert.Equal(500m * 6.00m * 0.03m, with.LtSideMeteringSurcharge);
        Assert.Equal(with.TotalRounded - without.TotalRounded, Math.Round(500m * 6.00m * 0.03m, 2));
    }

    [Fact]
    public void Monthly_maintenance_charges_are_prorated_like_the_fixed_charge()
    {
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("CHT"), 100m, 0m, TmcMonthly: 2000m, CpmcMonthly: 800m));
        Assert.Equal(2000m * 12m / 365m, b.Tmc);
        Assert.Equal(800m * 12m / 365m, b.Cpmc);
    }

    [Fact]
    public void An_FPPAS_share_can_reduce_the_bill()
    {
        var plain = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("DLT"), 1m, 20m));
        var credited = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("DLT"), 1m, 20m, FppasDailyShare: -28m));
        Assert.Equal(plain.Total - 28m, credited.Total);
    }

    [Fact]
    public void Electric_vehicle_charging_has_no_fixed_charge_and_no_rebate_change()
    {
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("EVLT"), 30m, DayKwh: 50m));
        Assert.Equal(0m, b.FixedCharge);
        Assert.Equal(300m, b.GrossEnergyCharge);   // 50 x 6.00
        Assert.Equal(6m, b.PrepaidRebate);          // 2%
    }

    [Fact]
    public void A_kVAh_schedule_without_a_kVAh_reading_falls_back_to_kWh_and_says_so()
    {
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("CHT"), 60m, 100m));
        Assert.Equal("kWh", b.BilledUnit);
        Assert.Contains(b.Notes, n => n.Contains("kVAh") && n.Contains("kWh was used"));
        Assert.Empty(DailyBillCalculator.Calculate(new DailyBillInput(Schedule("DLT"), 1m, 10m)).Notes);
    }

    [Fact]
    public void A_kVAh_schedule_is_billed_on_the_kVAh_reading_and_duty_stays_on_kWh()
    {
        // 100 kWh but 125 kVAh: the energy charge is on kVAh (CHT 6.00 -> 750.00), the duty on the 100 kWh units ("Others" 0.06 -> 6.00).
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("CHT"), 60m, DayKwh: 100m, DayKvah: 125m));
        Assert.Equal("kVAh", b.BilledUnit);
        Assert.Equal(125m, b.BilledEnergy);
        Assert.Equal(750.00m, b.GrossEnergyCharge);
        Assert.Equal(6.00m, b.ElectricityDuty);
        Assert.Empty(b.Notes);
    }

    [Fact]
    public void The_kVAh_slabs_continue_from_the_month_to_date_kVAh_not_the_kWh()
    {
        // ILT is flat 6.80, so use the month-to-date only to prove it is read: duty tiers (Industrial) follow kWh, energy follows kVAh.
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("ILT"), 10m, DayKwh: 20m, MonthToDateKwhBefore: 14_990m, DayKvah: 30m, MonthToDateKvahBefore: 16_000m));
        Assert.Equal(30m * 6.80m, b.GrossEnergyCharge);
        Assert.Equal(0.95m, b.ElectricityDuty); // 10 units at 0.05 and 10 at 0.045: still the kWh position
    }

    [Fact]
    public void A_kWh_schedule_ignores_a_kVAh_reading()
    {
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("DLT"), 1m, DayKwh: 30m, DayKvah: 40m));
        Assert.Equal("kWh", b.BilledUnit);
        Assert.Equal(150.00m, b.GrossEnergyCharge);
    }

    [Fact]
    public void Time_of_Day_energy_the_load_survey_does_not_cover_is_priced_at_Normal_and_says_so()
    {
        var bands = new Dictionary<string, decimal> { ["Normal"] = 500m, ["Peak"] = 300m };   // 800 of the day's 1,000 kVAh
        var b = DailyBillCalculator.Calculate(new DailyBillInput(Schedule("IHT"), 100m, DayKwh: 900m, DayKvah: 1000m, TouKvahByBand: bands));
        Assert.Equal(500m * 5.55m + 300m * 6.66m + 200m * 5.55m, b.GrossEnergyCharge);
        Assert.Contains(b.Notes, n => n.Contains("200") && n.Contains("no load survey interval"));
    }

    [Fact]
    public void Negative_inputs_are_refused()
    {
        var t = Schedule("DLT");
        Assert.Throws<ArgumentOutOfRangeException>(() => DailyBillCalculator.Calculate(new DailyBillInput(t, 1m, -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DailyBillCalculator.Calculate(new DailyBillInput(t, -1m, 1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DailyBillCalculator.Calculate(new DailyBillInput(t, 1m, 1m, MonthToDateKwhBefore: -1m)));
    }
}

/// <summary>The FY 2026-27 catalogue, checked against the tariff book's tables.</summary>
public class TariffCatalogue2026Tests
{
    private static TariffCatalogue2026.Entry Entry(string code) => TariffCatalogue2026.Build().Single(e => e.ScheduleCode == code);

    [Fact]
    public void Every_schedule_in_the_book_is_present_exactly_once()
    {
        var codes = TariffCatalogue2026.Build().Select(e => e.ScheduleCode).ToArray();
        Assert.Equal(new[] { "DLT", "CLT", "GP", "WSLT", "PL", "EVLT", "CRM", "AP", "ILT", "KJM", "DHT", "CHT", "BS", "WSHT", "IHT", "FAHT", "EVHT", "IEHT", "FAEHT" }, codes);
        Assert.Equal(codes.Length, TariffCatalogue2026.Build().Select(e => e.Name).Distinct().Count());
    }

    [Fact]
    public void Every_entry_builds_a_valid_tariff()
    {
        foreach (var e in TariffCatalogue2026.Build())
        {
            var t = TariffCatalogue2026.ToTariff(e);
            Assert.Equal(e.ScheduleCode, t.ScheduleCode);
            Assert.Equal(2.00m, t.PrepaidEnergyRebatePercent);
        }
    }

    [Theory]
    // code, fixed charge, first energy rate (or normal ToD rate)
    [InlineData("DLT", 90, 5.00)] [InlineData("CLT", 170, 7.45)] [InlineData("GP", 180, 7.35)] [InlineData("WSLT", 180, 7.00)]
    [InlineData("PL", 180, 7.00)] [InlineData("EVLT", 0, 6.00)] [InlineData("CRM", 55, 4.75)] [InlineData("AP", 130, 3.15)]
    [InlineData("ILT", 170, 6.80)] [InlineData("DHT", 350, 5.85)] [InlineData("CHT", 390, 6.00)] [InlineData("BS", 420, 6.10)]
    [InlineData("WSHT", 410, 6.60)] [InlineData("IHT", 340, 5.55)] [InlineData("FAHT", 500, 5.55)] [InlineData("EVHT", 0, 6.00)]
    [InlineData("IEHT", 500, 6.60)] [InlineData("FAEHT", 500, 5.60)]
    public void Fixed_charges_and_energy_rates_match_the_tariff_book(string code, double fixedCharge, double rate)
    {
        var t = TariffCatalogue2026.ToTariff(Entry(code));
        Assert.Equal((decimal)fixedCharge, t.FixedChargePerUnitPerMonth);
        var first = t.Slabs.Count > 0 ? t.Slabs.OrderBy(s => s.FromKwh).First().RatePerKwh : t.TouPeriods.Single(p => p.Label == "Normal").RatePerKvah;
        Assert.Equal((decimal)rate, first);
    }

    [Fact]
    public void Domestic_slabs_and_the_Kutir_Jyoti_slabs_are_as_published()
    {
        var dlt = TariffCatalogue2026.ToTariff(Entry("DLT"));
        Assert.Equal(new[] { 5.00m, 5.04m, 5.10m }, dlt.Slabs.OrderBy(s => s.FromKwh).Select(s => s.RatePerKwh));
        var kj = TariffCatalogue2026.ToTariff(Entry("KJM"));
        Assert.Equal(new[] { 4.57m, 5.00m, 5.04m, 5.10m }, kj.Slabs.OrderBy(s => s.FromKwh).Select(s => s.RatePerKwh));
        Assert.Equal(30m, kj.Slabs.OrderBy(s => s.FromKwh).First().UpToKwh);
    }

    [Theory]
    [InlineData("IHT", 5.55, 6.66, 4.72)]
    [InlineData("IEHT", 6.60, 7.92, 5.61)]
    public void Time_of_day_rates_are_as_published(string code, double normal, double peak, double offPeak)
    {
        var t = TariffCatalogue2026.ToTariff(Entry(code));
        Assert.Empty(t.Slabs);
        Assert.Equal((decimal)normal, t.TouPeriods.Single(p => p.Label == "Normal").RatePerKvah);
        Assert.Equal((decimal)peak, t.TouPeriods.Single(p => p.Label == "Peak").RatePerKvah);
        Assert.Equal((decimal)offPeak, t.TouPeriods.Single(p => p.Label == "Off-Peak").RatePerKvah);
        Assert.Equal("Peak", t.ClassifyTimeOfDay(new TimeSpan(18, 0, 0)));
        Assert.Equal("Off-Peak", t.ClassifyTimeOfDay(new TimeSpan(2, 0, 0)));
        Assert.Equal("Normal", t.ClassifyTimeOfDay(new TimeSpan(10, 0, 0)));
    }

    [Fact]
    public void Prepaid_facilities_follow_section_22()
    {
        var domestic = TariffCatalogue2026.ToTariff(Entry("DLT"));
        Assert.Equal(200m, domestic.EmergencyCreditLimit);
        // The book prints the ₹500 minimum only beside the General Purpose row, so other categories have no minimum.
        Assert.Equal((null, 15_000m, null, 25_000m), (domestic.MinVendAmountSinglePhase, domestic.MaxVendAmountSinglePhase!.Value, domestic.MinVendAmountThreePhase, domestic.MaxVendAmountThreePhase!.Value));
        Assert.Equal((100m, 200m), (domestic.InitialCreditSinglePhase!.Value, domestic.InitialCreditThreePhase!.Value));

        var gp = TariffCatalogue2026.ToTariff(Entry("GP"));
        Assert.Equal(2000m, gp.EmergencyCreditLimit);
        Assert.Equal((500m, 50_000m, 500m, 100_000m), (gp.MinVendAmountSinglePhase!.Value, gp.MaxVendAmountSinglePhase!.Value, gp.MinVendAmountThreePhase!.Value, gp.MaxVendAmountThreePhase!.Value));
        Assert.Equal((1_000m, 5_000m), (gp.InitialCreditSinglePhase!.Value, gp.InitialCreditThreePhase!.Value));

        // General Purpose Bulk Supply is also General Purpose for these facilities.
        Assert.Equal(2000m, TariffCatalogue2026.ToTariff(Entry("BS")).EmergencyCreditLimit);
    }

    [Fact]
    public void Classification_is_recorded()
    {
        Assert.Equal((VoltageLevel.HT, EnergyUnit.Kvah, FixedChargeBasis.PerKva), (Entry("DHT").Voltage, Entry("DHT").Unit, Entry("DHT").Basis));
        Assert.Equal((VoltageLevel.EHT, EnergyUnit.Kvah), (Entry("IEHT").Voltage, Entry("IEHT").Unit));
        Assert.Equal((VoltageLevel.HT, EnergyUnit.Kwh, FixedChargeBasis.None), (Entry("EVHT").Voltage, Entry("EVHT").Unit, Entry("EVHT").Basis));
        Assert.Equal(FixedChargeBasis.PerKwOrHp, Entry("AP").Basis);
        Assert.Equal(ConsumerCategory.PublicLighting, Entry("PL").Category);
    }

    [Fact]
    public void A_revision_keeps_the_classification_of_the_tariff_it_replaces()
    {
        var old = TariffCatalogue2026.ToTariff(Entry("DHT"));
        var revised = new Tariff(Guid.NewGuid(), old.Name, old.Category, new[] { new TariffSlab(0, null, 6.10m) }, 360m);
        Assert.Null(revised.ScheduleCode);
        revised.InheritClassification(old);
        Assert.Equal("DHT", revised.ScheduleCode);
        Assert.Equal(VoltageLevel.HT, revised.VoltageLevel);
        Assert.Equal(56m, revised.MinimumChargeableDemand);
    }
}

public class TariffBookCheckTests
{
    private static List<Tariff> AllFromBook() => TariffCatalogue2026.Build().Select(TariffCatalogue2026.ToTariff).ToList();

    [Fact]
    public void A_tariff_set_built_from_the_book_matches_the_book()
    {
        var rows = TariffBookCheck.Run(AllFromBook());
        Assert.Equal(19, rows.Count);
        Assert.All(rows, r => Assert.Equal("Matches", r.Status));
    }

    [Fact]
    public void A_changed_rate_is_reported_with_both_values()
    {
        var all = AllFromBook();
        var dlt = all.Single(t => t.ScheduleCode == "DLT");
        var changed = new Tariff(dlt.Id, dlt.Name, dlt.Category, new[] { new TariffSlab(0, 100, 5.70m), new TariffSlab(100, 200, 5.04m), new TariffSlab(200, null, 5.10m) },
            105m, 3m, 200m, 500m, 15_000m, 500m, 25_000m, null, dlt.ScheduleCode, dlt.VoltageLevel, dlt.EnergyUnit, dlt.FixedChargeBasis, dlt.MinimumChargeableDemand, 100m, 200m);
        all[all.FindIndex(t => t.ScheduleCode == "DLT")] = changed;

        var row = TariffBookCheck.Run(all).Single(r => r.ScheduleCode == "DLT");
        Assert.Equal("Differs", row.Status);
        Assert.Contains(row.Differences, d => d.Field == "Fixed charge" && d.Book == "90" && d.Current == "105");
        Assert.Contains(row.Differences, d => d.Field == "Prepaid rebate %" && d.Book == "2" && d.Current == "3");
        Assert.Contains(row.Differences, d => d.Field == "Energy slabs");
        Assert.All(TariffBookCheck.Run(all).Where(r => r.ScheduleCode != "DLT"), r => Assert.Equal("Matches", r.Status));
    }

    [Fact]
    public void A_schedule_with_no_tariff_is_missing()
    {
        var all = AllFromBook();
        all.RemoveAll(t => t.ScheduleCode == "PL");
        var row = TariffBookCheck.Run(all).Single(r => r.ScheduleCode == "PL");
        Assert.Equal("Missing", row.Status);
        Assert.Null(row.TariffId);
    }

    [Fact]
    public void A_tariff_created_before_schedule_codes_is_matched_by_name()
    {
        var all = AllFromBook();
        var legacy = TariffCatalogue2026.ToTariff(TariffCatalogue2026.Build()[0]);
        var noCode = new Tariff(legacy.Id, legacy.Name, legacy.Category, legacy.Slabs.Select(s => new TariffSlab(s.FromKwh, s.UpToKwh, s.RatePerKwh)), legacy.FixedChargePerUnitPerMonth,
            legacy.PrepaidEnergyRebatePercent, legacy.EmergencyCreditLimit, legacy.MinVendAmountSinglePhase, legacy.MaxVendAmountSinglePhase, legacy.MinVendAmountThreePhase, legacy.MaxVendAmountThreePhase,
            initialCreditSinglePhase: 100m, initialCreditThreePhase: 200m);
        all[all.FindIndex(t => t.ScheduleCode == "DLT")] = noCode;
        Assert.Equal("Matches", TariffBookCheck.Run(all).Single(r => r.ScheduleCode == "DLT").Status);
    }
}


public class TimeOfDaySplitTests
{
    private static Tariff Iht() => TariffCatalogue2026.ToTariff(TariffCatalogue2026.Build().Single(e => e.ScheduleCode == "IHT"));

    // IST = UTC + 5:30. 00:30 UTC is 06:00 IST (Normal starts); 11:30 UTC is 17:00 IST (Peak); 17:30 UTC is 23:00 IST (Off-Peak); 00:00 UTC is 05:30 IST (still Off-Peak).
    private static SurveyInterval At(int utcHour, int utcMinute, decimal kwh, decimal? kvah = null)
        => new(new DateTime(2026, 9, 21, utcHour, utcMinute, 0, DateTimeKind.Utc), kwh, kvah);

    [Fact]
    public void Each_interval_goes_to_the_band_its_start_falls_in_by_India_time()
    {
        var split = TimeOfDaySplit.Split(Iht(), new[] { At(0, 0, 10m), At(0, 30, 20m), At(11, 15, 30m), At(11, 30, 40m), At(17, 15, 50m), At(17, 30, 60m) }, 210m, billedInKvah: false)!;
        Assert.Equal(10m + 60m, split.ByBand["Off-Peak"]);         // 05:30 and 23:00 IST
        Assert.Equal(20m + 30m, split.ByBand["Normal"]);           // 06:00 and 16:45 IST
        Assert.Equal(40m + 50m, split.ByBand["Peak"]);             // 17:00 and 22:45 IST
        Assert.Null(split.Note);
    }

    [Fact]
    public void A_kVAh_tariff_uses_the_interval_kVAh_when_every_interval_has_it()
    {
        var split = TimeOfDaySplit.Split(Iht(), new[] { At(2, 0, 10m, 12m), At(12, 0, 20m, 25m) }, 37m, billedInKvah: true)!;
        Assert.Equal(12m, split.ByBand["Normal"]);   // 07:30 IST
        Assert.Equal(25m, split.ByBand["Peak"]);     // 17:30 IST
        Assert.Null(split.Note);
    }

    [Fact]
    public void Intervals_with_only_kWh_give_shares_that_are_applied_to_the_days_kVAh()
    {
        // 25% of the kWh is Normal, 75% Peak; the day's billed kVAh is 200, so 50 and 150, and the note says how.
        var split = TimeOfDaySplit.Split(Iht(), new[] { At(2, 0, 10m), At(12, 0, 30m) }, 200m, billedInKvah: true)!;
        Assert.Equal(50m, split.ByBand["Normal"]);
        Assert.Equal(150m, split.ByBand["Peak"]);
        Assert.Contains("kWh but no kVAh", split.Note);
    }

    [Fact]
    public void No_intervals_means_no_split()
    {
        Assert.Null(TimeOfDaySplit.Split(Iht(), Array.Empty<SurveyInterval>(), 100m, billedInKvah: true));
        Assert.Null(TimeOfDaySplit.Split(Iht(), new[] { At(2, 0, 0m) }, 100m, billedInKvah: true));   // a day of zero kWh has no shares
    }

    [Fact]
    public void A_tariff_without_bands_has_no_split()
        => Assert.Null(TimeOfDaySplit.Split(TariffCatalogue2026.ToTariff(TariffCatalogue2026.Build().Single(e => e.ScheduleCode == "DLT")), new[] { At(2, 0, 5m) }, 5m, false));
}
