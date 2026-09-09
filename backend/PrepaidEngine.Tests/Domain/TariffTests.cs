using PrepaidEngine.Domain.Entities;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class TariffTests
{
    private static Tariff BuildSlabTariff() => new(
        Guid.NewGuid(),
        "Domestic",
        new[]
        {
            new TariffSlab(0, 100, 5.00m),
            new TariffSlab(100, 200, 7.50m),
            new TariffSlab(200, null, 10.00m)
        });

    [Theory]
    [InlineData(50, 250.00)]      // fully within first slab
    [InlineData(100, 500.00)]     // exactly at first boundary
    [InlineData(150, 875.00)]     // spans first two slabs
    [InlineData(250, 1750.00)]    // spans all three slabs
    public void CalculateAmount_AppliesSlabsCorrectly(decimal consumptionKwh, decimal expectedAmount)
    {
        var tariff = BuildSlabTariff();

        var amount = tariff.CalculateAmount(consumptionKwh);

        Assert.Equal(expectedAmount, amount);
    }

    [Fact]
    public void CalculateAmount_ZeroConsumption_ReturnsZero()
    {
        var tariff = BuildSlabTariff();

        Assert.Equal(0m, tariff.CalculateAmount(0));
    }
}
