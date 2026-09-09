namespace PrepaidEngine.Domain;

/// <summary>
/// Result of applying a payment (a bill payment or a prepaid recharge) against a consumer's
/// outstanding arrears.
/// </summary>
/// <param name="AmountAppliedToArrears">Portion of the payment recovered against arrears.</param>
/// <param name="RemainingArrears">Arrears still outstanding after this payment.</param>
/// <param name="AmountRemainingForCurrentChargesOrCredit">
/// Whatever's left of the payment after arrear recovery — to be applied to the current bill
/// (postpaid) or credited as spendable wallet balance (prepaid).
/// </param>
public record ArrearRecoveryResult(
    decimal AmountAppliedToArrears,
    decimal RemainingArrears,
    decimal AmountRemainingForCurrentChargesOrCredit);

/// <summary>
/// Arrear recovery: deciding how much of an incoming payment goes toward a consumer's
/// pre-existing outstanding arrears before the rest is available for current charges (postpaid)
/// or spendable credit (prepaid).
///
/// Two distinct rules are in play here, from two different-strength sources — treat them
/// separately, do not conflate them:
///
/// <list type="bullet">
/// <item><description><b>Verified, tariff book §13.4</b>: "Any payment made by the consumer
/// shall first be adjusted towards the arrears including LPS and then current bills." This is
/// an <i>uncapped</i>, arrears-first rule for ordinary bill payment — the whole payment is
/// eligible to clear arrears before anything is treated as covering current charges. This is
/// the default behavior of <see cref="Calculate"/> when no cap is supplied.</description></item>
/// <item><description><b>Unverified, RFP-only</b>: the supplied MePDCL AMI/RDSS RFP describes,
/// as an "indicative rule", that no more than 50% of a prepaid recharge may be applied toward
/// arrears. This is explicitly not confirmed against the tariff book and must never be
/// hard-coded as a default — <see cref="Calculate"/> only applies a cap when the caller
/// supplies <paramref name="maxRecoveryPercentOfPayment"/> explicitly, e.g. from
/// utility-specific configuration, and 50% specifically must come from that configuration, not
/// from this class.</description></item>
/// </list>
///
/// Neither reference calculation workbook has a non-zero arrears example to verify a specific
/// numeric scenario against — see docs/tariff-validation-report.md.
/// </summary>
public static class ArrearRecovery
{
    public static ArrearRecoveryResult Calculate(decimal paymentAmount, decimal outstandingArrears, decimal? maxRecoveryPercentOfPayment = null)
    {
        if (paymentAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(paymentAmount));
        if (outstandingArrears < 0)
            throw new ArgumentOutOfRangeException(nameof(outstandingArrears));
        if (maxRecoveryPercentOfPayment is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxRecoveryPercentOfPayment), "Must be between 0 and 100.");

        // No cap supplied -> the tariff book's §13.4 default: the whole payment is eligible to
        // clear arrears first.
        var recoveryCeiling = maxRecoveryPercentOfPayment.HasValue
            ? paymentAmount * (maxRecoveryPercentOfPayment.Value / 100m)
            : paymentAmount;

        var amountAppliedToArrears = Math.Min(outstandingArrears, recoveryCeiling);
        var remainingArrears = outstandingArrears - amountAppliedToArrears;
        var amountRemaining = paymentAmount - amountAppliedToArrears;

        return new ArrearRecoveryResult(amountAppliedToArrears, remainingArrears, amountRemaining);
    }
}
