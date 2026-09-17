using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// An Instantaneous Profile (IP) reading — the meter's point-in-time electrical state (voltage,
/// current, power, power factor, frequency) and relay contact status, used for meter-health and
/// instantaneous-load intelligence only. IP is never a billing input and is never aggregated into
/// consumption — <see cref="DailyLoadProfile"/> alone drives billing.
///
/// Unique per <c>MeterId + Timestamp</c> (IP is a meter-level reading; it is not itself tied to a
/// billing consumer relationship the way DLP/BP/LS are, though the current consumer is denormalized
/// here for query convenience).
/// </summary>
public class InstantaneousReading
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid MeterId { get; private set; }
    public DateTime Timestamp { get; private set; }
    public decimal VoltageVolts { get; private set; }
    public decimal CurrentAmps { get; private set; }
    public decimal PowerKw { get; private set; }
    public decimal PowerFactor { get; private set; }
    public decimal FrequencyHz { get; private set; }
    public MeterRelayStatus RelayStatus { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public string? SourceReference { get; private set; }

    public InstantaneousReading(
        Guid id,
        Guid consumerId,
        Guid meterId,
        DateTime timestamp,
        decimal voltageVolts,
        decimal currentAmps,
        decimal powerKw,
        decimal powerFactor,
        decimal frequencyHz,
        MeterRelayStatus relayStatus,
        DateTime receivedAt,
        string? sourceReference = null)
    {
        if (voltageVolts < 0) throw new ArgumentOutOfRangeException(nameof(voltageVolts));
        if (currentAmps < 0) throw new ArgumentOutOfRangeException(nameof(currentAmps));
        if (powerFactor < -1 || powerFactor > 1) throw new ArgumentOutOfRangeException(nameof(powerFactor), "Power factor must be between -1 and 1.");
        if (frequencyHz < 0) throw new ArgumentOutOfRangeException(nameof(frequencyHz));

        Id = id;
        ConsumerId = consumerId;
        MeterId = meterId;
        Timestamp = timestamp;
        VoltageVolts = voltageVolts;
        CurrentAmps = currentAmps;
        PowerKw = powerKw;
        PowerFactor = powerFactor;
        FrequencyHz = frequencyHz;
        RelayStatus = relayStatus;
        ReceivedAt = receivedAt;
        SourceReference = sourceReference;
    }

    // EF Core / serialization
    private InstantaneousReading()
    {
    }
}
