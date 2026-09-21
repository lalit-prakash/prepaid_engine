using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A smart meter installed at a consumer's service point, tracking cumulative consumption.
/// </summary>
public class SmartMeter
{
    public Guid Id { get; private set; }
    public string MeterNumber { get; private set; }
    public MeterPhase Phase { get; private set; }
    public decimal LastReadingKwh { get; private set; }
    public DateTime? LastReadingAt { get; private set; }

    /// <summary>When data from this meter (a load profile, register reading, interval, instantaneous reading, event or alarm) last reached
    /// the engine; null when none ever has. Kept up to date by <c>MeterCommunication.TouchAsync</c> on ingestion and used to tell communicating
    /// meters from silent ones. Never set by generated (provisional) profiles, which are not the meter talking.</summary>
    public DateTime? LastCommunicatedAt { get; private set; }

    public SmartMeter(Guid id, string meterNumber, MeterPhase phase)
    {
        if (string.IsNullOrWhiteSpace(meterNumber))
            throw new ArgumentException("Meter number is required.", nameof(meterNumber));

        Id = id;
        MeterNumber = meterNumber;
        Phase = phase;
        LastReadingKwh = 0m;
    }

    // EF Core / serialization
    private SmartMeter()
    {
        MeterNumber = string.Empty;
    }

    /// <summary>
    /// Records a new cumulative reading and returns the consumption (kWh) since the last reading.
    /// </summary>
    public decimal RecordReading(decimal cumulativeKwh, DateTime readAt)
    {
        if (cumulativeKwh < LastReadingKwh)
            throw new ArgumentOutOfRangeException(nameof(cumulativeKwh), "Reading cannot be lower than the previous reading.");

        var consumption = cumulativeKwh - LastReadingKwh;
        LastReadingKwh = cumulativeKwh;
        LastReadingAt = readAt;
        return consumption;
    }
}
