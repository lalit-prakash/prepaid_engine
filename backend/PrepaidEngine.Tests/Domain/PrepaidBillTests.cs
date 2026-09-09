using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class PrepaidBillTests
{
    private static PrepaidBill BuildBill(decimal fppasAmount = 0m, Guid? fppasChargeId = null, decimal tmcAmount = 0m, decimal cpmcAmount = 0m) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        energyChargeGross: 225.00m,
        prepaidRebateAmount: 4.50m,
        fixedCharge: 180.00m,
        electricityDutyAmount: 2.25m,
        generatedAt: DateTime.UtcNow,
        fppasAmount: fppasAmount,
        fppasChargeId: fppasChargeId,
        tmcAmount: tmcAmount,
        cpmcAmount: cpmcAmount);

    [Fact]
    public void Amount_ComposesFromEnergyNetPlusFixedPlusDutyPlusFppas_NoFppas()
    {
        var bill = BuildBill();

        // (225.00 - 4.50) + 180.00 + 2.25 + 0 = 402.75
        Assert.Equal(220.50m, bill.EnergyChargeNet);
        Assert.Equal(402.75m, bill.Amount);
    }

    [Fact]
    public void Amount_IncludesPositiveFppasShare()
    {
        var fppasId = Guid.NewGuid();
        var bill = BuildBill(fppasAmount: 4.50m, fppasChargeId: fppasId);

        // 402.75 + 4.50 = 407.25
        Assert.Equal(407.25m, bill.Amount);
        Assert.Equal(fppasId, bill.FppasChargeId);
    }

    [Fact]
    public void Amount_IncludesNegativeFppasShare()
    {
        var bill = BuildBill(fppasAmount: -30.00m);

        // 402.75 - 30.00 = 372.75
        Assert.Equal(372.75m, bill.Amount);
    }

    [Fact]
    public void Amount_IncludesTmcAndCpmc()
    {
        var bill = BuildBill(tmcAmount: 2000.00m, cpmcAmount: 800.00m);

        // 402.75 + 2000.00 + 800.00 = 3202.75
        Assert.Equal(3202.75m, bill.Amount);
        Assert.Equal(2000.00m, bill.TmcAmount);
        Assert.Equal(800.00m, bill.CpmcAmount);
    }

    [Fact]
    public void Amount_ComposesAllSixOptionalAndCoreComponentsTogether()
    {
        // An HT consumer with an opted-in transformer and CT-PT set, plus a positive FPPAS
        // share, all landing on the same bill — proving every component wires together
        // correctly, not just each one in isolation.
        var tmc = TransformerMaintenanceCharge.CalculateForExclusiveUse(SupplyVoltage.Kv11, optedForMepdclMaintenance: true, installedTransformerCapacityKva: 100m);
        var cpmc = CtPtMaintenanceCharge.Calculate(SupplyVoltage.Kv11, CtPtWiring.ThreePhaseThreeWire, optedForMepdclMaintenance: true);

        var bill = BuildBill(fppasAmount: 10.00m, tmcAmount: tmc, cpmcAmount: cpmc);

        // 402.75 + 10.00 (fppas) + 2000.00 (tmc: 100 * 20) + 800.00 (cpmc) = 3212.75
        Assert.Equal(2000.00m, tmc);
        Assert.Equal(800.00m, cpmc);
        Assert.Equal(3212.75m, bill.Amount);
    }

    [Fact]
    public void Constructor_NegativeTmc_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrepaidBill(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            energyChargeGross: 100m, prepaidRebateAmount: 0m,
            fixedCharge: 0m, electricityDutyAmount: 0m, generatedAt: DateTime.UtcNow,
            tmcAmount: -1m));
    }

    [Fact]
    public void Constructor_NegativeCpmc_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrepaidBill(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            energyChargeGross: 100m, prepaidRebateAmount: 0m,
            fixedCharge: 0m, electricityDutyAmount: 0m, generatedAt: DateTime.UtcNow,
            cpmcAmount: -1m));
    }

    [Fact]
    public void Constructor_RebateExceedingGross_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrepaidBill(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            energyChargeGross: 100m, prepaidRebateAmount: 101m,
            fixedCharge: 0m, electricityDutyAmount: 0m, generatedAt: DateTime.UtcNow));
    }

    [Fact]
    public void Constructor_NegativeFppasLargerThanRestOfBill_ThrowsRatherThanProducingCreditNote()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrepaidBill(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            energyChargeGross: 100m, prepaidRebateAmount: 0m,
            fixedCharge: 10m, electricityDutyAmount: 5m, generatedAt: DateTime.UtcNow,
            fppasAmount: -1000m));
    }

    [Fact]
    public void ApplyPayment_And_OutstandingAmount_StillWorkWithFppasIncluded()
    {
        var bill = BuildBill(fppasAmount: 4.50m);

        bill.ApplyPayment(200m);

        Assert.Equal(207.25m, bill.OutstandingAmount);
        Assert.Equal(PrepaidEngine.Domain.Enums.BillStatus.PartiallyPaid, bill.Status);

        bill.ApplyPayment(bill.OutstandingAmount);

        Assert.Equal(0m, bill.OutstandingAmount);
        Assert.Equal(PrepaidEngine.Domain.Enums.BillStatus.Paid, bill.Status);
    }
}
