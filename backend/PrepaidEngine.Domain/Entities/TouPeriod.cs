namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A Time-of-Day (ToD/TOU) rate band within a <see cref="Tariff"/> — e.g. "Peak, 17:00-23:00,
/// ₹6.66/kVAh". Sourced from the MePDCL Electricity Distribution Tariff's ToD schedules for
/// Industrial HT (IHT) and Industrial EHT (IEHT), the only categories the tariff book defines
/// ToD for.
///
/// <see cref="EndTime"/> may be earlier than <see cref="StartTime"/>, meaning the period wraps
/// past midnight (e.g. the tariff book's Off-peak band, 23:00-06:00).
///
/// Rates are stored verbatim from the tariff book rather than derived from a "Normal" rate by
/// formula at request time: the book documents Peak/Off-peak as "+20%"/"-15%" of Normal for
/// context, but its own published Off-peak figures are already rounded by the utility (e.g.
/// IHT: 5.55 × 0.85 = 4.7175, published as 4.72) — treating the three published numbers as the
/// source of truth avoids silently drifting from what's actually gazetted.
/// </summary>
public class TouPeriod
{
    public Guid Id { get; private set; }
    public string Label { get; private set; }
    public TimeSpan StartTime { get; private set; }
    public TimeSpan EndTime { get; private set; }
    public decimal RatePerKvah { get; private set; }

    public TouPeriod(string label, TimeSpan startTime, TimeSpan endTime, decimal ratePerKvah)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Label is required.", nameof(label));
        if (startTime < TimeSpan.Zero || startTime >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(startTime));
        if (endTime < TimeSpan.Zero || endTime >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(endTime));
        if (startTime == endTime)
            throw new ArgumentException("A ToD period cannot start and end at the same time (it would be either empty or span the whole day — use two periods, or 00:00-00:00 is not supported).", nameof(endTime));
        if (ratePerKvah < 0)
            throw new ArgumentOutOfRangeException(nameof(ratePerKvah));

        Id = Guid.NewGuid();
        Label = label;
        StartTime = startTime;
        EndTime = endTime;
        RatePerKvah = ratePerKvah;
    }

    // EF Core / serialization
    private TouPeriod()
    {
        Label = string.Empty;
    }

    /// <summary>
    /// True if <paramref name="timeOfDay"/> falls within this period, correctly handling a
    /// period that wraps past midnight (<see cref="EndTime"/> &lt; <see cref="StartTime"/>).
    /// The end boundary is exclusive, matching the tariff book's own adjacent bands (e.g.
    /// Normal ends at 17:00 exactly where Peak begins).
    /// </summary>
    public bool Contains(TimeSpan timeOfDay)
    {
        if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(timeOfDay));

        return StartTime < EndTime
            ? timeOfDay >= StartTime && timeOfDay < EndTime
            : timeOfDay >= StartTime || timeOfDay < EndTime; // wraps past midnight
    }
}
