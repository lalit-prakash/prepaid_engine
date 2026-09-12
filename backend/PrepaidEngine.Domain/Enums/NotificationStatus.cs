namespace PrepaidEngine.Domain.Enums;

/// <summary>Delivery status of a <see cref="Entities.NotificationEvent"/>. This project has no
/// real SMS gateway (see the entity's own doc comment) — Sent here means "queued for a real
/// dispatcher to pick up", not "an SMS actually left this system".</summary>
public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
}
