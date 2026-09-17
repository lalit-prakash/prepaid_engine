using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// An informational meter event (power fail/restore, communication fail/restore, relay
/// open/close, clock change) — logged for history and analytics only. Never requires operator
/// acknowledgement; contrast <see cref="MeterAlarm"/>, which does and is deliberately kept as a
/// separate entity/table rather than merged into this one.
///
/// Unique per <c>MeterId + EventCode + EventTimestamp</c>.
/// </summary>
public class MeterEvent
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid MeterId { get; private set; }
    public MeterEventCode EventCode { get; private set; }
    public DateTime EventTimestamp { get; private set; }
    public string? Description { get; private set; }
    public MeterEventStatus Status { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public string? SourceReference { get; private set; }

    public MeterEvent(
        Guid id,
        Guid consumerId,
        Guid meterId,
        MeterEventCode eventCode,
        DateTime eventTimestamp,
        DateTime receivedAt,
        string? description = null,
        string? sourceReference = null)
    {
        Id = id;
        ConsumerId = consumerId;
        MeterId = meterId;
        EventCode = eventCode;
        EventTimestamp = eventTimestamp;
        Description = description;
        ReceivedAt = receivedAt;
        SourceReference = sourceReference;
        Status = MeterEventStatus.Received;
    }

    // EF Core / serialization
    private MeterEvent()
    {
    }

    public void MarkProcessed() => Status = MeterEventStatus.Processed;
}
