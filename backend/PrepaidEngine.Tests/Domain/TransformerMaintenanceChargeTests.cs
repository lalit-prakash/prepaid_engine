using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class TransformerMaintenanceChargeTests
{
    [Theory]
    [InlineData(SupplyVoltage.Kv11, 100, 2000.00)]
    [InlineData(SupplyVoltage.Kv33, 100, 2000.00)]
    [InlineData(SupplyVoltage.Kv132, 100, 2500.00)]
    public void CalculateForExclusiveUse_AppliesVoltageRate_WhenOptedIn(SupplyVoltage voltage, decimal installedCapacityKva, decimal expected)
    {
        Assert.Equal(expected, TransformerMaintenanceCharge.CalculateForExclusiveUse(voltage, optedForMepdclMaintenance: true, installedCapacityKva));
    }

    [Theory]
    [InlineData(SupplyVoltage.Kv11)]
    [InlineData(SupplyVoltage.Kv33)]
    [InlineData(SupplyVoltage.Kv132)]
    public void CalculateForExclusiveUse_ReturnsZero_WhenNotOptedIn(SupplyVoltage voltage)
    {
        Assert.Equal(0m, TransformerMaintenanceCharge.CalculateForExclusiveUse(voltage, optedForMepdclMaintenance: false, installedTransformerCapacityKva: 500m));
    }

    [Fact]
    public void CalculateForSharedUse_BillsOnContractedDemandOrConnectedLoad_NotInstalledCapacity()
    {
        // Even if the transformer's own installed capacity is much larger, shared-use billing
        // uses the consumer's contracted demand/connected load instead (§5.2).
        var tmc = TransformerMaintenanceCharge.CalculateForSharedUse(
            SupplyVoltage.Kv11, optedForMepdclMaintenance: true, contractedDemandOrConnectedLoadKva: 50m);

        Assert.Equal(1000.00m, tmc); // 50 * 20
    }

    [Fact]
    public void Calculate_NegativeBasis_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TransformerMaintenanceCharge.CalculateForExclusiveUse(SupplyVoltage.Kv11, true, -1m));
    }
}
