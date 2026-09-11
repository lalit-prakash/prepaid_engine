namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// An immutable, append-only record of a tracked operational or configuration change: who (or
/// what actor string — this project has no per-user identity yet, see
/// docs/assumptions-and-security.md) did what, to which entity, when, and (where relevant) the
/// old/new value. Unlike every other entity in this project, there is no state machine here —
/// once constructed an <see cref="AuditEntry"/> never changes, which is the entire point of an
/// audit log.
///
/// Appended (see the triggers documented in README.md) whenever: an RC/DC command is dispatched,
/// a conversion request is decided/completed, a tariff version is recorded, or an operational
/// exception is resolved.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; }
    public string EntityType { get; }
    public string EntityId { get; }
    public string Action { get; }
    public string Actor { get; }
    public string? OldValue { get; }
    public string? NewValue { get; }
    public string? Details { get; }
    public DateTime OccurredAt { get; }

    public AuditEntry(
        Guid id,
        string entityType,
        string entityId,
        string action,
        string actor,
        DateTime occurredAt,
        string? oldValue = null,
        string? newValue = null,
        string? details = null)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type is required.", nameof(entityType));
        if (string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("Entity id is required.", nameof(entityId));
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action is required.", nameof(action));
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Actor is required.", nameof(actor));

        Id = id;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        Actor = actor;
        OccurredAt = occurredAt;
        OldValue = oldValue;
        NewValue = newValue;
        Details = details;
    }

    // EF Core / serialization
    private AuditEntry()
    {
        EntityType = string.Empty;
        EntityId = string.Empty;
        Action = string.Empty;
        Actor = string.Empty;
    }
}
