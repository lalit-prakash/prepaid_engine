using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

/// <summary>
/// Regression tests reproducing real day-by-day rows from MePDCL's own reference calculation
/// workbook ("Prepaid Calculation Category wise.xlsx", sheet "DLT" — consumer 1000888034,
/// meter AS9042987, 1 kW DLT, August 2026 billing cycle). Values are taken verbatim from that
/// workbook, not invented, so a mismatch here means our calculation diverges from the utility's
/// own reference implementation, not a made-up expectation.
/// </summary>
public class TariffGoldenDataTests
{
    private static Tariff BuildDltTariff() => new(
        Guid.NewGuid(),
        "MePDCL Domestic (DLT)",
        ConsumerCategory.Domestic,
        new[]
        {
            new TariffSlab(0, 100, 5.00m),
            new TariffSlab(100, 200, 5.04m),
            new TariffSlab(200, null, 5.10m)
        },
        fixedChargePerUnitPerMonth: 90.00m,
        prepaidEnergyRebatePercent: 2.00m);

    private const decimal ConnectedLoadKw = 1m;

    // (previousCumulative, currentCumulative, expected gross EC, expected rebate, expected net EC, expected duty)
    // Source rows: 2026-08-01, 2026-08-11 (crosses into slab 2), 2026-08-22 (crosses into slab 3),
    // 2026-08-25 (zero consumption — fixed charge/duty still computed by caller, not by this method).
    [Theory]
    [InlineData(0, 6, 30.00, 0.60, 29.40, 0.30)]
    [InlineData(90, 102, 60.08, 1.2016, 58.8784, 0.60)]
    [InlineData(193, 201, 40.38, 0.8076, 39.5724, 0.40)]
    [InlineData(222, 222, 0.00, 0.00, 0.00, 0.00)]
    public void CalculateEnergyChargeForPeriod_MatchesMepdclReferenceWorkbook(
        decimal previousCumulativeKwh, decimal currentCumulativeKwh,
        decimal expectedGrossEc, decimal expectedRebate, decimal expectedNetEc, decimal expectedDuty)
    {
        var tariff = BuildDltTariff();

        var grossEc = tariff.CalculateEnergyChargeForPeriod(previousCumulativeKwh, currentCumulativeKwh);
        var rebate = grossEc * (tariff.PrepaidEnergyRebatePercent / 100m);
        var netEc = grossEc - rebate;
        var duty = ElectricityDuty.CalculateForPeriod(tariff.Category, previousCumulativeKwh, currentCumulativeKwh);

        Assert.Equal(expectedGrossEc, grossEc, 4);
        Assert.Equal(expectedRebate, rebate, 4);
        Assert.Equal(expectedNetEc, netEc, 4);
        Assert.Equal(expectedDuty, duty, 4);
    }

    [Fact]
    public void CalculateDailyFixedCharge_MatchesMepdclReferenceWorkbook()
    {
        var tariff = BuildDltTariff();

        // Workbook shows 2.958904109589041 for every day of the cycle (90 * 1 * 12 / 365),
        // including the zero-consumption days at the end of August.
        var daily = tariff.CalculateDailyFixedCharge(ConnectedLoadKw);

        Assert.Equal(2.958904109589041m, daily, 12);
    }

    [Theory]
    [InlineData(0, 6, 32.65890410958904)]      // 2026-08-01
    [InlineData(90, 102, 62.43730410958904)]   // 2026-08-11 (crosses slab 1 -> 2)
    [InlineData(193, 201, 42.93130410958904)]  // 2026-08-22 (crosses slab 2 -> 3)
    [InlineData(222, 222, 2.958904109589041)]  // 2026-08-25 (zero consumption; fixed charge still accrues)
    public void NetBill_MatchesMepdclReferenceWorkbook(decimal previousCumulativeKwh, decimal currentCumulativeKwh, decimal expectedNetBill)
    {
        var tariff = BuildDltTariff();

        var grossEc = tariff.CalculateEnergyChargeForPeriod(previousCumulativeKwh, currentCumulativeKwh);
        var rebate = grossEc * (tariff.PrepaidEnergyRebatePercent / 100m);
        var netEc = grossEc - rebate;
        var duty = ElectricityDuty.CalculateForPeriod(tariff.Category, previousCumulativeKwh, currentCumulativeKwh);
        var fixedCharge = tariff.CalculateDailyFixedCharge(ConnectedLoadKw);

        var netBill = netEc + fixedCharge + duty;

        Assert.Equal(expectedNetBill, netBill, 6);
    }
}

/// <summary>
/// BPL/Kutir Jyoti is modeled as an ordinary 4-slab <see cref="Tariff"/> — the "first 30 kWh at
/// the special rate, excess billed at the normal domestic slab" rule from the tariff book
/// (§A.1, Kutir Jyoti/BPL note) turns out to be exactly a standard cumulative slab table once
/// the DLT slab boundaries (100, 200) are kept absolute and only the lowest tier (0-30) gets
/// the special rate — confirmed by the slab table in MePDCL's own reference workbook
/// ("Prepaid bill calculation.xlsx", sheet "BPL_Prepaid Calculation"), which lists exactly
/// this 4-tier table rather than a separate "special case then restart" calculation. No new
/// domain code was needed for this — the existing slab-cumulative <see cref="Tariff"/> already
/// handles it.
/// </summary>
public class BplTariffTests
{
    private static Tariff BuildBplTariff() => new(
        Guid.NewGuid(),
        "MePDCL Kutir Jyoti/BPL (Metered KJM)",
        ConsumerCategory.KutirJyotiBpl,
        new[]
        {
            new TariffSlab(0, 30, 4.57m),
            new TariffSlab(30, 100, 5.00m),
            new TariffSlab(100, 200, 5.04m),
            new TariffSlab(200, null, 5.10m)
        });

    [Theory]
    [InlineData(0, 0.00)]
    [InlineData(30, 137.10)]     // fully within the special BPL slab: 30 * 4.57
    [InlineData(50, 237.10)]     // 30 * 4.57 + 20 * 5.00
    [InlineData(100, 487.10)]    // 30 * 4.57 + 70 * 5.00
    [InlineData(200, 991.10)]    // + 100 * 5.04
    [InlineData(250, 1246.10)]   // + 50 * 5.10
    public void CalculateEnergyCharge_AppliesBplSlabsThenNormalDomesticSlabs(decimal consumptionKwh, decimal expected)
    {
        var tariff = BuildBplTariff();

        Assert.Equal(expected, tariff.CalculateEnergyCharge(consumptionKwh));
    }
}

/// <summary>
/// Documents a real, verified discrepancy between MePDCL's own reference workbook and the
/// currently effective MePDCL tariff book, per the project owner's explicit instruction:
/// current tariff-book value = production; Excel value = legacy/reference only, never
/// silently substituted. See docs/tariff-validation-report.md.
/// </summary>
public class DhtTariffDiscrepancyTests
{
    private static Tariff BuildDhtTariff(decimal energyChargePerKvah) => new(
        Guid.NewGuid(),
        $"DHT ({energyChargePerKvah:0.00}/kVAh)",
        ConsumerCategory.Domestic,
        new[] { new TariffSlab(0, null, energyChargePerKvah) },
        fixedChargePerUnitPerMonth: 350.00m,
        prepaidEnergyRebatePercent: 2.00m);

    [Fact]
    public void CurrentTariffBookProductionValue_Is5_85PerKvah()
    {
        // Source: MePDCL Electricity Distribution Tariff, effective 1 Apr 2026,
        // "2. Standard Rates for High Tension (HT) Category" — Domestic HT (DHT): ₹5.85/kVAh.
        // This is the value the system must use in production.
        var productionTariff = BuildDhtTariff(5.85m);

        Assert.Equal(585.00m, productionTariff.CalculateEnergyCharge(100));
    }

    [Fact]
    public void LegacyExcelReferenceValue_Is5_87PerKvah_NotUsedInProduction()
    {
        // Source: "Prepaid bill calculation.xlsx", sheet "DHT PREPAID" — shows ₹5.87/kVAh and
        // an example bill of ₹587 gross EC for 100 kVAh. This is a known discrepancy against
        // the current tariff book (₹5.85) and is retained here ONLY as a reference/regression
        // fixture for that workbook — it must never be used as the live production rate.
        var legacyExcelTariff = BuildDhtTariff(5.87m);

        Assert.Equal(587.00m, legacyExcelTariff.CalculateEnergyCharge(100));
    }
}
