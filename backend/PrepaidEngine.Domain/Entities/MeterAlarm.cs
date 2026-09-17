using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// An alarm-worthy meter condition (tamper, magnetic influence, cover open, reverse energy,
/// voltage/current abnormality) — always carries a severity and requires an operator
/// acknowledge/resolve workflow, unlike a plain <see cref="MeterEvent"/>. Kept as its own entity
/// (own table) rather than merged with <see cref="MeterEvent"/> so the two can have independent
/// lifecycles and UI screens per the platform's domain-ownership rules.
/// </summary>
public class MeterAlarm
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid MeterId { get; private set; }
    public MeterAlarmCode AlarmCode { get; private set; }
    public MeterAlarmSeverity Severity { get; private set; }
    public DateTime RaisedAt { get; private set; }
    public MeterAlarmStatus Status { get; private set; }
    public DateTime? AcknowledgedAt { get; private set; }
    public string? AcknowledgedBy { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public string? ResolutionNote { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public string? SourceReference { get; private set; }

    public MeterAlarm(
        Guid id,
        Guid consumerId,
        Guid meterId,
        MeterAlarmCode alarmCode,
        MeterAlarmSeverity severity,
        DateTime raisedAt,
        DateTime receivedAt,
        string? sourceReference = null)
    {
        Id = id;
        ConsumerId = consumerId;
        MeterId = meterId;
        AlarmCode = alarmCode;
        Severity = severity;
        RaisedAt = raisedAt;
        ReceivedAt = receivedAt;
        SourceReference = sourceReference;
        Status = MeterAlarmStatus.Open;
    }

    // EF Core / serialization
    private MeterAlarm()
    {
    }

    public void Acknowledge(string acknowledgedBy, DateTime acknowledgedAt)
    {
        if (Status != MeterAlarmStatus.Open)
            throw new InvalidOperationException($"Only an Open alarm can be acknowledged (current status: {Status}).");
        if (string.IsNullOrWhiteSpace(acknowledgedBy))
            throw new ArgumentException("Acknowledging an alarm requires an operator identity.", nameof(acknowledgedBy));

        Status = MeterAlarmStatus.Acknowledged;
        AcknowledgedAt = acknowledgedAt;
        AcknowledgedBy = acknowledgedBy;
    }

    public void Resolve(string resolutionNote, DateTime resolvedAt)
    {
        if (Status == MeterAlarmStatus.Resolved)
            throw new InvalidOperationException("This alarm is already resolved.");
        if (string.IsNullOrWhiteSpace(resolutionNote))
            throw new ArgumentException("Resolving an alarm requires a resolution note.", nameof(resolutionNote));

        Status = MeterAlarmStatus.Resolved;
        ResolvedAt = resolvedAt;
        ResolutionNote = resolutionNote;
    }
}
