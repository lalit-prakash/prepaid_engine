namespace PrepaidEngine.Domain.Enums;

/// <summary>What triggered a <see cref="Entities.NotificationEvent"/>.</summary>
public enum NotificationEventType
{
    LowBalance,
    EmergencyCredit,
    DisconnectionEligible,
    BillingProvisional,

    /// <summary>Sent once a postpaid→prepaid conversion completes (payment-mode-change command
    /// Acknowledged) — "you are now in prepaid mode".</summary>
    PrepaidConversionCompleted,

    /// <summary>Sent when the emergency-credit guard auto-dispatches a Disconnect command because
    /// the wallet fell at/below the emergency-credit limit.</summary>
    AutoDisconnected,

    /// <summary>Sent when the emergency-credit guard auto-dispatches a Reconnect command because
    /// a recharge/adjustment brought the wallet back to a positive balance.</summary>
    AutoReconnected,
}
