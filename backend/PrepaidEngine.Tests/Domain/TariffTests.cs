using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class TariffTests
{
    private static Tariff BuildSlabTariff() => new(
        Guid.NewGuid(),
        "Domestic",
        ConsumerCategory.Domestic,
        new[]
        {
            new TariffSlab(0, 100, 5.00m),
            new TariffSlab(100, 200, 7.50m),
            new TariffSlab(200, null, 10.00m)
        });

    // Modeled on MePDCL's actual Domestic LT (DLT) schedule: ₹90/kW fixed charge,
    // ₹5.00/5.04/5.10 slabs, 2% prepaid energy rebate (Meghalaya Distribution Tariff,
    // effective 1 Apr 2026).
    private static Tariff BuildMepdclDomesticTariff() => new(
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
        prepaidEnergyRebatePercent: 2.00m,
        emergencyCreditLimit: 200.00m,
        minVendAmountSinglePhase: 500.00m,
        maxVendAmountSinglePhase: 15000.00m,
        minVendAmountThreePhase: 500.00m,
        maxVendAmountThreePhase: 25000.00m);

    [Theory]
    [InlineData(50, 250.00)]      // fully within first slab
    [InlineData(100, 500.00)]     // exactly at first boundary
    [InlineData(150, 875.00)]     // spans first two slabs
    [InlineData(250, 1750.00)]    // spans all three slabs
    public void CalculateEnergyCharge_AppliesSlabsCorrectly(decimal consumptionKwh, decimal expectedAmount)
    {
        var tariff = BuildSlabTariff();

        var amount = tariff.CalculateEnergyCharge(consumptionKwh);

        Assert.Equal(expectedAmount, amount);
    }

    [Fact]
    public void CalculateEnergyCharge_ZeroConsumption_ReturnsZero()
    {
        var tariff = BuildSlabTariff();

        Assert.Equal(0m, tariff.CalculateEnergyCharge(0));
    }

    [Fact]
    public void CalculateFixedCharge_MultipliesRateByConnectedLoad()
    {
        var tariff = BuildMepdclDomesticTariff();

        Assert.Equal(360.00m, tariff.CalculateFixedCharge(4m)); // 90.00 * 4 kW
    }

    [Fact]
    public void CalculateNetPrepaidCharge_AppliesRebateThenAddsFixedCharge()
    {
        var tariff = BuildMepdclDomesticTariff();

        // 50 kWh -> energy charge 250.00, 2% rebate = 5.00 -> 245.00; fixed charge 90*2=180.00
        var net = tariff.CalculateNetPrepaidCharge(consumptionKwh: 50m, connectedLoadOrContractDemand: 2m);

        Assert.Equal(425.00m, net);
    }

    [Theory]
    [InlineData(MeterPhase.SinglePhase, 500.00, true)]
    [InlineData(MeterPhase.SinglePhase, 499.99, false)]
    [InlineData(MeterPhase.SinglePhase, 15000.00, true)]
    [InlineData(MeterPhase.SinglePhase, 15000.01, false)]
    [InlineData(MeterPhase.ThreePhase, 25000.00, true)]
    [InlineData(MeterPhase.ThreePhase, 25000.01, false)]
    public void ValidateVendAmount_EnforcesPerPhaseLimits(MeterPhase phase, decimal amount, bool expectedValid)
    {
        var tariff = BuildMepdclDomesticTariff();

        if (expectedValid)
        {
            var exception = Record.Exception(() => tariff.ValidateVendAmount(amount, phase));
            Assert.Null(exception);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => tariff.ValidateVendAmount(amount, phase));
        }
    }

    [Fact]
    public void Constructor_MinVendGreaterThanMax_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Tariff(
            Guid.NewGuid(),
            "Invalid",
            ConsumerCategory.Domestic,
            new[] { new TariffSlab(0, null, 5.00m) },
            minVendAmountSinglePhase: 1000m,
            maxVendAmountSinglePhase: 500m));
    }

    [Fact]
    public void Constructor_DefaultsToActiveStatus()
    {
        var tariff = BuildSlabTariff();
        Assert.Equal(TariffLifecycleStatus.Active, tariff.Status);
    }

    [Fact]
    public void Retire_FromActive_SetsRetiredStatus()
    {
        var tariff = BuildSlabTariff();
        tariff.Retire();
        Assert.Equal(TariffLifecycleStatus.Retired, tariff.Status);
    }

    [Fact]
    public void Retire_AlreadyRetired_Throws()
    {
        var tariff = BuildSlabTariff();
        tariff.Retire();
        Assert.Throws<InvalidOperationException>(() => tariff.Retire());
    }
}
