namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Whether a consumer is currently billed prepaid (this engine's core scope) or postpaid.
/// Changed only through <see cref="Entities.Consumer.ConvertToPrepaid"/>/
/// <see cref="Entities.Consumer.ConvertToPostpaid"/> — never a raw property set — so a
/// conversion is always a deliberate, auditable action, matching this project's
/// command/acknowledgement discipline for other consumer-state transitions.
/// </summary>
public enum BillingMode
{
    Prepaid,
    Postpaid,
}
