using PrepaidEngine.Domain.Entities;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class PrepaidBillTests
{
    private static PrepaidBill BuildBill(decimal fppasAmount = 0m, Guid? fppasChargeId = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        energyChargeGross: 225.00m,
        prepaidRebateAmount: 4.50m,
        fixedCharge: 180.00m,
        electricityDutyAmount: 2.25m,
        generatedAt: DateTime.UtcNow,
        fppasAmount: fppasAmount,
        fppasChargeId: fppasChargeId);

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
