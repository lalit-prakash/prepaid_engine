using PrepaidEngine.Domain;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class ArrearRecoveryTests
{
    // --- Tariff book §13.4 default: uncapped, arrears-first ---

    [Fact]
    public void Calculate_NoCap_PaymentFullyCoversArrears_RemainderAvailableForCurrentCharges()
    {
        var result = ArrearRecovery.Calculate(paymentAmount: 500m, outstandingArrears: 300m);

        Assert.Equal(300m, result.AmountAppliedToArrears);
        Assert.Equal(0m, result.RemainingArrears);
        Assert.Equal(200m, result.AmountRemainingForCurrentChargesOrCredit);
    }

    [Fact]
    public void Calculate_NoCap_PaymentSmallerThanArrears_EntirePaymentAppliedToArrears()
    {
        var result = ArrearRecovery.Calculate(paymentAmount: 200m, outstandingArrears: 500m);

        Assert.Equal(200m, result.AmountAppliedToArrears);
        Assert.Equal(300m, result.RemainingArrears);
        Assert.Equal(0m, result.AmountRemainingForCurrentChargesOrCredit);
    }

    [Fact]
    public void Calculate_NoArrears_EntirePaymentAvailableForCurrentChargesOrCredit()
    {
        var result = ArrearRecovery.Calculate(paymentAmount: 500m, outstandingArrears: 0m);

        Assert.Equal(0m, result.AmountAppliedToArrears);
        Assert.Equal(0m, result.RemainingArrears);
        Assert.Equal(500m, result.AmountRemainingForCurrentChargesOrCredit);
    }

    // --- RFP-indicative, caller-supplied cap (e.g. 50% of a prepaid recharge) ---

    [Fact]
    public void Calculate_WithCap_LimitsRecoveryToPercentageOfPayment_EvenWhenMoreArrearsAreOwed()
    {
        // Rs 1000 recharge, Rs 2000 arrears owed, capped at 50% of the recharge -> only Rs 500
        // goes to arrears no matter how much is owed.
        var result = ArrearRecovery.Calculate(paymentAmount: 1000m, outstandingArrears: 2000m, maxRecoveryPercentOfPayment: 50m);

        Assert.Equal(500m, result.AmountAppliedToArrears);
        Assert.Equal(1500m, result.RemainingArrears);
        Assert.Equal(500m, result.AmountRemainingForCurrentChargesOrCredit);
    }

    [Fact]
    public void Calculate_WithCap_ArrearsSmallerThanCapped_OnlyRecoversWhatIsOwed()
    {
        // Cap allows up to Rs 500, but only Rs 300 is actually owed -> recover just Rs 300.
        var result = ArrearRecovery.Calculate(paymentAmount: 1000m, outstandingArrears: 300m, maxRecoveryPercentOfPayment: 50m);

        Assert.Equal(300m, result.AmountAppliedToArrears);
        Assert.Equal(0m, result.RemainingArrears);
        Assert.Equal(700m, result.AmountRemainingForCurrentChargesOrCredit);
    }

    [Fact]
    public void Calculate_ZeroPercentCap_NeverRecoversArrears()
    {
        var result = ArrearRecovery.Calculate(paymentAmount: 1000m, outstandingArrears: 500m, maxRecoveryPercentOfPayment: 0m);

        Assert.Equal(0m, result.AmountAppliedToArrears);
        Assert.Equal(500m, result.RemainingArrears);
        Assert.Equal(1000m, result.AmountRemainingForCurrentChargesOrCredit);
    }

    [Fact]
    public void Calculate_HundredPercentCap_BehavesLikeNoCap()
    {
        var capped = ArrearRecovery.Calculate(500m, 300m, maxRecoveryPercentOfPayment: 100m);
        var uncapped = ArrearRecovery.Calculate(500m, 300m);

        Assert.Equal(uncapped, capped);
    }

    // --- Validation ---

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Calculate_NegativeAmounts_Throw(decimal paymentAmount, decimal outstandingArrears)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArrearRecovery.Calculate(paymentAmount, outstandingArrears));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Calculate_CapOutsideZeroToHundred_Throws(decimal cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArrearRecovery.Calculate(100m, 100m, cap));
    }
}
