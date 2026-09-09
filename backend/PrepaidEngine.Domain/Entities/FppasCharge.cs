namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// FPPAS (Fuel and Power Purchase Adjustment Surcharge) is a separate charge component from
/// the energy charge (tariff §A.4) — it is never folded into the energy charge itself.
///
/// Algorithm and every number below are sourced from MePDCL's own reference calculation
/// workbook ("Prepaid bill calculation.xlsx", sheet "FPPAS Calculation"), not invented:
/// <list type="number">
/// <item><description>A bill dated the 1st of month M reflects consumption from month M-1,
/// with a known gross energy charge for that period.</description></item>
/// <item><description>Some time during month M, the utility notifies an FPPAS rate (a signed
/// fraction, e.g. -0.14 for -14%, +0.0665 for +6.65%) tied to that M-1 energy charge.</description></item>
/// <item><description>The resulting amount (<c>EnergyCharge × Rate</c>) is <b>not</b> applied
/// to bill M — it is deferred and billed starting with the bill dated the 1st of month M+1,
/// evenly prorated across every calendar day of that month.</description></item>
/// </list>
/// Verified against two worked examples in the source workbook: a negative-FAC case
/// (-14% on a ₹6,000 energy charge = -₹840, spread over June's 30 days = -₹28.00/day, exactly)
/// and a positive-FAC case (+6.65% on a ₹3,250 energy charge = ₹216.125, spread over October's
/// 31 days = ₹6.971774193548387.../day).
/// </summary>
public class FppasCharge
{
    public Guid Id { get; private set; }

    /// <summary>The prior month's gross energy charge this FPPAS rate is computed against.</summary>
    public decimal SourceEnergyCharge { get; private set; }

    /// <summary>Signed fraction, not a percentage (e.g. -0.14 for -14%, 0.0665 for +6.65%).</summary>
    public decimal RateFraction { get; private set; }

    public DateTime NotifiedAt { get; private set; }

    /// <summary>The total FPPAS amount for this notification: <c>SourceEnergyCharge × RateFraction</c>. Can be negative.</summary>
    public decimal TotalAmount => SourceEnergyCharge * RateFraction;

    public FppasCharge(Guid id, decimal sourceEnergyCharge, decimal rateFraction, DateTime notifiedAt)
    {
        if (sourceEnergyCharge < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceEnergyCharge));

        Id = id;
        SourceEnergyCharge = sourceEnergyCharge;
        RateFraction = rateFraction;
        NotifiedAt = notifiedAt;
    }

    // EF Core / serialization
    private FppasCharge()
    {
    }

    /// <summary>
    /// The billing month this FPPAS charge must actually be applied to, given the bill date of
    /// the source energy charge it was computed from: one calendar month after that bill date
    /// (i.e. two months after the underlying consumption month — see the worked examples in
    /// the type-level documentation).
    /// </summary>
    public static DateTime DetermineApplicableBillingMonth(DateTime sourceBillDate) =>
        new DateTime(sourceBillDate.Year, sourceBillDate.Month, 1).AddMonths(1);

    /// <summary>
    /// Splits <see cref="TotalAmount"/> evenly across every day of the target billing month,
    /// at full decimal precision with no per-day rounding — matching the source workbook
    /// exactly (its October example shows the unrounded repeating decimal
    /// 6.971774193548387... on every one of the 31 days, not a rounded-then-adjusted figure).
    /// Prefer this for reproducing/explaining a bill line-by-line; prefer
    /// <see cref="AllocateAcrossDaysRoundedToCents"/> when actually posting money to a ledger,
    /// where amounts must reconcile to the paisa.
    /// </summary>
    public IReadOnlyList<decimal> AllocateAcrossDays(int daysInBillingMonth)
    {
        if (daysInBillingMonth <= 0)
            throw new ArgumentOutOfRangeException(nameof(daysInBillingMonth));

        var dailyAmount = TotalAmount / daysInBillingMonth;
        return Enumerable.Repeat(dailyAmount, daysInBillingMonth).ToList();
    }

    /// <summary>
    /// Splits <see cref="TotalAmount"/> across every day of the target billing month, rounded
    /// to the nearest paisa (2 decimal places), with the last day absorbing whatever residual
    /// rounding leaves so that the allocations sum to <see cref="TotalAmount"/> exactly to the
    /// cent — required for actually posting FPPAS to a wallet/bill ledger, where "close enough"
    /// isn't good enough for money.
    /// </summary>
    public IReadOnlyList<decimal> AllocateAcrossDaysRoundedToCents(int daysInBillingMonth)
    {
        if (daysInBillingMonth <= 0)
            throw new ArgumentOutOfRangeException(nameof(daysInBillingMonth));

        var roundedTotal = Math.Round(TotalAmount, 2, MidpointRounding.AwayFromZero);
        var dailyRounded = Math.Round(TotalAmount / daysInBillingMonth, 2, MidpointRounding.AwayFromZero);

        var allocations = new decimal[daysInBillingMonth];
        for (var i = 0; i < daysInBillingMonth - 1; i++)
            allocations[i] = dailyRounded;

        // Last day absorbs the rounding residual so the total reconciles exactly.
        allocations[daysInBillingMonth - 1] = roundedTotal - dailyRounded * (daysInBillingMonth - 1);

        return allocations;
    }
}
