namespace PrepaidEngine.Domain.Enums;

/// <summary>What triggered a <see cref="Entities.NotificationEvent"/>, per the LS/DLP billing
/// pipeline spec section 18 (the current set this engine actually raises).</summary>
public enum NotificationEventType
{
    LowBalance,
    EmergencyCredit,
    DisconnectionEligible,
    BillingProvisional,
}
