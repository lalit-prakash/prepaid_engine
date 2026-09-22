namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// One monthly FPPAS rate as the utility notifies it (tariff book §A.4: "billed to the consumers on a monthly basis") —
/// the tariff-master record the daily run reads, as opposed to <see cref="FppasCharge"/>, which is the per-consumer amount
/// worked out from this rate and one consumer's own prior month's energy charge (see <see cref="ApplicableBillingMonth"/>
/// and the worked mechanism documented on <see cref="FppasCharge"/>).
/// </summary>
public class FppasRateNotification
{
    public Guid Id { get; private set; }

    /// <summary>Signed fraction, not a percentage (e.g. -0.14 for -14%, 0.0665 for +6.65%).</summary>
    public decimal RateFraction { get; private set; }

    public DateTime NotifiedAt { get; private set; }

    /// <summary>
    /// The first calendar day of the billing month this rate is actually applied in: one month after
    /// <see cref="NotifiedAt"/>'s month, per the deferred-by-one-month mechanism on <see cref="FppasCharge"/>. Unique —
    /// a later notification for the same billing month replaces the earlier one rather than stacking (see
    /// <see cref="Renotify"/>).
    /// </summary>
    public DateOnly ApplicableBillingMonth { get; private set; }

    public FppasRateNotification(Guid id, decimal rateFraction, DateTime notifiedAt)
    {
        Id = id;
        RateFraction = rateFraction;
        NotifiedAt = notifiedAt;
        ApplicableBillingMonth = ApplicableMonthFor(notifiedAt);
    }

    // EF Core / serialization
    private FppasRateNotification()
    {
    }

    public static DateOnly ApplicableMonthFor(DateTime notifiedAt) =>
        new DateOnly(notifiedAt.Year, notifiedAt.Month, 1).AddMonths(1);

    /// <summary>Corrects a mistaken notification for the same billing month (the rate itself, or when it was notified).</summary>
    public void Renotify(decimal rateFraction, DateTime notifiedAt)
    {
        if (ApplicableMonthFor(notifiedAt) != ApplicableBillingMonth)
            throw new ArgumentException("The new notification date must fall in the same notification month as the one it replaces.", nameof(notifiedAt));

        RateFraction = rateFraction;
        NotifiedAt = notifiedAt;
    }
}
