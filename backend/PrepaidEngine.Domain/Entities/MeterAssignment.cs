using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// An audit record of a physical meter identity change for a consumer (install/replace/remove).
/// A meter swap is a physical-meter-identity boundary, not merely a reading reset — the point of
/// this entity is so that a cumulative reading from the old meter is never subtracted from a
/// cumulative reading on the new meter to "compute consumption" (see
/// <see cref="MeterAssignmentEventType.Replaced"/>'s closing/opening readings below).
/// <see cref="DailyLoadProfile"/> already scopes every reading to a specific <c>MeterId</c>, so
/// sequence-continuity checks naturally never compare across two different physical meters as
/// long as this history — not <see cref="Entities.SmartMeter"/> alone — is treated as the source
/// of truth for "which meter was this consumer's, when".
/// </summary>
public class MeterAssignment
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid? OldMeterId { get; private set; }
    public Guid NewMeterId { get; private set; }
    public MeterAssignmentEventType EventType { get; private set; }
    public DateTime EffectiveFrom { get; private set; }
    public decimal? OldMeterClosingReadingKwh { get; private set; }
    public decimal NewMeterOpeningReadingKwh { get; private set; }
    public string? Reason { get; private set; }
    public DateTime RecordedAt { get; private set; }

    public MeterAssignment(
        Guid id,
        Guid consumerId,
        Guid? oldMeterId,
        Guid newMeterId,
        MeterAssignmentEventType eventType,
        DateTime effectiveFrom,
        decimal? oldMeterClosingReadingKwh,
        decimal newMeterOpeningReadingKwh,
        DateTime recordedAt,
        string? reason = null)
    {
        if (newMeterOpeningReadingKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(newMeterOpeningReadingKwh), "Opening reading cannot be negative.");
        if (eventType == MeterAssignmentEventType.Replaced && oldMeterId is null)
            throw new ArgumentException("A Replaced event requires the old meter's id.", nameof(oldMeterId));

        Id = id;
        ConsumerId = consumerId;
        OldMeterId = oldMeterId;
        NewMeterId = newMeterId;
        EventType = eventType;
        EffectiveFrom = effectiveFrom;
        OldMeterClosingReadingKwh = oldMeterClosingReadingKwh;
        NewMeterOpeningReadingKwh = newMeterOpeningReadingKwh;
        Reason = reason;
        RecordedAt = recordedAt;
    }

    // EF Core / serialization
    private MeterAssignment()
    {
    }
}
