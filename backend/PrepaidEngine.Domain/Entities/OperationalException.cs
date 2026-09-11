using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// Something that needs operator attention: a failed/timed-out <see cref="MeterCommand"/> or a
/// failed/timed-out <see cref="ConnectivityCommand"/>. Created automatically alongside the
/// triggering event (never hand-entered) so nothing that needs attention can go unlisted, and
/// resolved only with a mandatory note — the same mandatory-reason discipline this project
/// already applies to <see cref="ConnectivityCommand.Reason"/>.
/// </summary>
public class OperationalException
{
    public Guid Id { get; private set; }
    public OperationalExceptionSourceType SourceType { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid ConsumerId { get; private set; }
    public string Description { get; private set; }
    public OperationalExceptionStatus Status { get; private set; }
    public string? ResolutionNote { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    public OperationalException(
        Guid id,
        OperationalExceptionSourceType sourceType,
        Guid sourceId,
        Guid consumerId,
        string description,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A description is required for an operational exception.", nameof(description));

        Id = id;
        SourceType = sourceType;
        SourceId = sourceId;
        ConsumerId = consumerId;
        Description = description;
        Status = OperationalExceptionStatus.Open;
        CreatedAt = createdAt;
    }

    // EF Core / serialization
    private OperationalException()
    {
        Description = string.Empty;
    }

    public void Resolve(string resolutionNote, DateTime resolvedAt)
    {
        if (Status != OperationalExceptionStatus.Open)
            throw new InvalidOperationException($"Cannot resolve an operational exception that is {Status} — only an Open exception can be resolved.");
        if (string.IsNullOrWhiteSpace(resolutionNote))
            throw new ArgumentException("A resolution note is required to resolve an operational exception.", nameof(resolutionNote));

        Status = OperationalExceptionStatus.Resolved;
        ResolutionNote = resolutionNote;
        ResolvedAt = resolvedAt;
    }
}
