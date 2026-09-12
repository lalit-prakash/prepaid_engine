using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A queued consumer notification (low balance, emergency credit, disconnection eligibility, or
/// provisional-billing data-quality hold), per the LS/DLP billing pipeline spec §18-19.
///
/// This project has no real SMS gateway — this entity is a persistence/queue layer only. Marking
/// one <see cref="NotificationStatus.Sent"/> means "a real dispatcher would pick this up next",
/// never "an SMS actually left this system" — matching this project's "no fake success states"
/// discipline. A real production dispatcher (outbox pattern, retries, delivery callbacks) is an
/// explicitly documented future integration point, not something this entity fakes.
/// </summary>
public class NotificationEvent
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public NotificationEventType EventType { get; private set; }
    public string Message { get; private set; }
    public NotificationStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public string? ProviderReference { get; private set; }

    public NotificationEvent(Guid id, Guid consumerId, NotificationEventType eventType, string message, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A message is required.", nameof(message));

        Id = id;
        ConsumerId = consumerId;
        EventType = eventType;
        Message = message;
        Status = NotificationStatus.Pending;
        CreatedAt = createdAt;
    }

    // EF Core / serialization
    private NotificationEvent()
    {
        Message = string.Empty;
    }

    public void MarkSent(DateTime sentAt, string? providerReference = null)
    {
        Status = NotificationStatus.Sent;
        SentAt = sentAt;
        ProviderReference = providerReference;
    }

    public void MarkFailed() => Status = NotificationStatus.Failed;
}
