namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A consumption slab within a <see cref="Tariff"/>, e.g. "0-100 kWh @ 5.00/unit".
/// <see cref="UpToKwh"/> of <c>null</c> means the slab is unbounded (applies to all remaining consumption).
/// </summary>
public class TariffSlab
{
    public decimal FromKwh { get; private set; }
    public decimal? UpToKwh { get; private set; }
    public decimal RatePerKwh { get; private set; }

    public TariffSlab(decimal fromKwh, decimal? upToKwh, decimal ratePerKwh)
    {
        if (fromKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(fromKwh));
        if (upToKwh.HasValue && upToKwh.Value <= fromKwh)
            throw new ArgumentException("Slab upper bound must be greater than its lower bound.", nameof(upToKwh));
        if (ratePerKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(ratePerKwh));

        FromKwh = fromKwh;
        UpToKwh = upToKwh;
        RatePerKwh = ratePerKwh;
    }

    // EF Core / serialization
    private TariffSlab()
    {
    }

    /// <summary>
    /// Portion of <paramref name="totalConsumptionKwh"/> that falls within this slab.
    /// </summary>
    public decimal KwhWithinSlab(decimal totalConsumptionKwh)
    {
        if (totalConsumptionKwh <= FromKwh)
            return 0m;

        var upperBound = UpToKwh ?? totalConsumptionKwh;
        var effectiveUpper = Math.Min(totalConsumptionKwh, upperBound);
        return Math.Max(0m, effectiveUpper - FromKwh);
    }
}
