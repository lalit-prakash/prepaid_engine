using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class ElectricityDutyTests
{
    [Theory]
    [InlineData(0, 0.00)]
    [InlineData(1, 0.05)]
    [InlineData(100, 5.00)] // matches MePDCL's own DHT reference workbook example (100 units -> Rs 5 duty)
    public void Calculate_DomesticAndBpl_FlatFivePaisaPerUnit(decimal consumptionKwh, decimal expectedDuty)
    {
        Assert.Equal(expectedDuty, ElectricityDuty.Calculate(ConsumerCategory.Domestic, consumptionKwh));
        Assert.Equal(expectedDuty, ElectricityDuty.Calculate(ConsumerCategory.KutirJyotiBpl, consumptionKwh));
    }

    [Theory]
    [InlineData(0, 0.00)]
    [InlineData(1, 0.06)]
    [InlineData(100, 6.00)]
    public void Calculate_OtherCategories_FlatSixPaisaPerUnit(decimal consumptionKwh, decimal expectedDuty)
    {
        Assert.Equal(expectedDuty, ElectricityDuty.Calculate(ConsumerCategory.NonDomestic, consumptionKwh));
        Assert.Equal(expectedDuty, ElectricityDuty.Calculate(ConsumerCategory.GeneralPurpose, consumptionKwh));
    }

    [Theory]
    [InlineData(0, 0.00)]
    [InlineData(1, 0.05)]
    [InlineData(15000, 750.00)]         // exactly the first-slab boundary: 15000 * 0.05
    [InlineData(15000.01, 750.00045)]   // 0.01 unit into the second slab @ 0.045
    [InlineData(40000, 1875.00)]        // exactly the second-slab boundary: 750 + 25000 * 0.045
    [InlineData(40000.01, 1875.0003)]   // 0.01 unit into the third slab @ 0.03
    public void Calculate_Industrial_AppliesTieredSlabs(decimal consumptionKwh, decimal expectedDuty)
    {
        Assert.Equal(expectedDuty, ElectricityDuty.Calculate(ConsumerCategory.Industrial, consumptionKwh), 4);
    }

    [Fact]
    public void CalculateForPeriod_Industrial_AppliesSlabsToCumulativePosition_NotToRawDelta()
    {
        // A day's consumption of 100 units, starting from a cumulative position already past
        // the first 15,000-unit slab, must bill at the second-slab rate (0.045), not restart at
        // the first-slab rate as it would if slabs were naively applied to the daily delta alone.
        var duty = ElectricityDuty.CalculateForPeriod(ConsumerCategory.Industrial, previousCumulativeKwh: 15000, currentCumulativeKwh: 15100);

        Assert.Equal(100 * 0.045m, duty);
    }

    [Fact]
    public void Calculate_NegativeConsumption_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ElectricityDuty.Calculate(ConsumerCategory.Domestic, -1));
    }
}
