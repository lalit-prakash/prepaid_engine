using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

/// <summary>
/// Regression tests reproducing the MePDCL tariff book's ToD (Time-of-Day) schedules for
/// Industrial HT (IHT) and Industrial EHT (IEHT) verbatim — the only two categories the tariff
/// book defines ToD for.
/// </summary>
public class TouTariffTests
{
    // Source: tariff book, "3.*The Time-of-Day (ToD) Tariff for IHT consumers is as follows".
    private static Tariff BuildIhtTariff() => new(
        Guid.NewGuid(),
        "MePDCL Industrial HT (IHT)",
        ConsumerCategory.Industrial,
        slabs: Array.Empty<TariffSlab>(),
        fixedChargePerUnitPerMonth: 340.00m,
        touPeriods: new[]
        {
            new TouPeriod("Normal", TimeSpan.FromHours(6), TimeSpan.FromHours(17), 5.55m),
            new TouPeriod("Peak", TimeSpan.FromHours(17), TimeSpan.FromHours(23), 6.66m),
            new TouPeriod("Off-Peak", TimeSpan.FromHours(23), TimeSpan.FromHours(6), 4.72m),
        });

    // Source: tariff book, "*The Time-of-Day (ToD) Tariff for IEHT consumers is as follows".
    private static Tariff BuildIehtTariff() => new(
        Guid.NewGuid(),
        "MePDCL Industrial EHT (IEHT)",
        ConsumerCategory.Industrial,
        slabs: Array.Empty<TariffSlab>(),
        fixedChargePerUnitPerMonth: 500.00m,
        touPeriods: new[]
        {
            new TouPeriod("Normal", TimeSpan.FromHours(6), TimeSpan.FromHours(17), 6.60m),
            new TouPeriod("Peak", TimeSpan.FromHours(17), TimeSpan.FromHours(23), 7.92m),
            new TouPeriod("Off-Peak", TimeSpan.FromHours(23), TimeSpan.FromHours(6), 5.61m),
        });

    [Theory]
    [InlineData("05:59", "Off-Peak")]
    [InlineData("06:00", "Normal")]
    [InlineData("16:59", "Normal")]
    [InlineData("17:00", "Peak")]
    [InlineData("22:59", "Peak")]
    [InlineData("23:00", "Off-Peak")]
    public void ClassifyTimeOfDay_MatchesTariffBookBoundaries(string time, string expectedLabel)
    {
        var tariff = BuildIhtTariff();

        Assert.Equal(expectedLabel, tariff.ClassifyTimeOfDay(TimeSpan.Parse(time)));
    }

    [Fact]
    public void ClassifyTimeOfDay_OffPeakWrapsPastMidnight()
    {
        var tariff = BuildIhtTariff();

        Assert.Equal("Off-Peak", tariff.ClassifyTimeOfDay(TimeSpan.FromHours(0)));
        Assert.Equal("Off-Peak", tariff.ClassifyTimeOfDay(new TimeSpan(3, 30, 0)));
    }

    [Fact]
    public void CalculateTouEnergyCharge_Iht_AppliesEachBandsOwnRate()
    {
        var tariff = BuildIhtTariff();

        var charge = tariff.CalculateTouEnergyCharge(new Dictionary<string, decimal>
        {
            ["Normal"] = 1000m,
            ["Peak"] = 200m,
            ["Off-Peak"] = 300m
        });

        // 1000*5.55 + 200*6.66 + 300*4.72 = 5550 + 1332 + 1416 = 8298
        Assert.Equal(8298.00m, charge);
    }

    [Fact]
    public void CalculateTouEnergyCharge_Ieht_AppliesEachBandsOwnRate()
    {
        var tariff = BuildIehtTariff();

        var charge = tariff.CalculateTouEnergyCharge(new Dictionary<string, decimal>
        {
            ["Normal"] = 1000m,
            ["Peak"] = 200m,
            ["Off-Peak"] = 300m
        });

        // 1000*6.60 + 200*7.92 + 300*5.61 = 6600 + 1584 + 1683 = 9867
        Assert.Equal(9867.00m, charge);
    }

    [Fact]
    public void CalculateTouEnergyCharge_MissingPeriodInDictionary_TreatedAsZeroConsumption()
    {
        var tariff = BuildIhtTariff();

        var charge = tariff.CalculateTouEnergyCharge(new Dictionary<string, decimal> { ["Normal"] = 100m });

        Assert.Equal(555.00m, charge); // Peak/Off-Peak absent -> 0
    }

    [Fact]
    public void CalculateTouEnergyCharge_UnknownPeriodLabel_Throws()
    {
        var tariff = BuildIhtTariff();

        Assert.Throws<ArgumentException>(() => tariff.CalculateTouEnergyCharge(
            new Dictionary<string, decimal> { ["Weekend"] = 100m }));
    }

    [Fact]
    public void CalculateTouEnergyCharge_NegativeConsumption_Throws()
    {
        var tariff = BuildIhtTariff();

        Assert.Throws<ArgumentOutOfRangeException>(() => tariff.CalculateTouEnergyCharge(
            new Dictionary<string, decimal> { ["Normal"] = -1m }));
    }

    [Fact]
    public void ClassifyTimeOfDay_NoTouPeriodsConfigured_Throws()
    {
        var dltTariff = new Tariff(Guid.NewGuid(), "DLT-No-Tou", ConsumerCategory.Domestic,
            new[] { new TariffSlab(0, null, 5.00m) });

        Assert.Throws<InvalidOperationException>(() => dltTariff.ClassifyTimeOfDay(TimeSpan.FromHours(10)));
    }

    [Fact]
    public void Constructor_NoSlabsAndNoTouPeriods_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Tariff(
            Guid.NewGuid(), "Empty", ConsumerCategory.Industrial, Array.Empty<TariffSlab>()));
    }
}
