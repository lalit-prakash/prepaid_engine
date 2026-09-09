using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class CtPtMaintenanceChargeTests
{
    [Theory]
    [InlineData(SupplyVoltage.Kv11, CtPtWiring.ThreePhaseThreeWire, 800.00)]
    [InlineData(SupplyVoltage.Kv11, CtPtWiring.ThreePhaseFourWire, 1000.00)]
    [InlineData(SupplyVoltage.Kv33, CtPtWiring.ThreePhaseThreeWire, 1500.00)]
    [InlineData(SupplyVoltage.Kv33, CtPtWiring.ThreePhaseFourWire, 1900.00)]
    public void Calculate_AppliesTariffBookRate_WhenOptedIn(SupplyVoltage voltage, CtPtWiring wiring, decimal expected)
    {
        Assert.Equal(expected, CtPtMaintenanceCharge.Calculate(voltage, wiring, optedForMepdclMaintenance: true));
    }

    [Theory]
    [InlineData(SupplyVoltage.Kv11, CtPtWiring.ThreePhaseThreeWire)]
    [InlineData(SupplyVoltage.Kv33, CtPtWiring.ThreePhaseFourWire)]
    public void Calculate_ReturnsZero_WhenNotOptedIn(SupplyVoltage voltage, CtPtWiring wiring)
    {
        Assert.Equal(0m, CtPtMaintenanceCharge.Calculate(voltage, wiring, optedForMepdclMaintenance: false));
    }

    [Theory]
    [InlineData(CtPtWiring.ThreePhaseThreeWire)]
    [InlineData(CtPtWiring.ThreePhaseFourWire)]
    public void Calculate_132kV_HasNoDefinedRate_Throws(CtPtWiring wiring)
    {
        // The tariff book defines CPMC only for 11 kV and 33 kV.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CtPtMaintenanceCharge.Calculate(SupplyVoltage.Kv132, wiring, optedForMepdclMaintenance: true));
    }

    [Fact]
    public void Calculate_132kV_NotOptedIn_ReturnsZeroWithoutThrowing()
    {
        // Opt-out is checked before the rate lookup, so an unpriced voltage never surfaces as
        // an error when the charge wouldn't be levied anyway.
        Assert.Equal(0m, CtPtMaintenanceCharge.Calculate(SupplyVoltage.Kv132, CtPtWiring.ThreePhaseThreeWire, optedForMepdclMaintenance: false));
    }
}
