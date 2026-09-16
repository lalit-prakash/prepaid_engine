namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.PaymentModeChangeCommand"/> — a separate enum from every
/// other command lifecycle in this project (never shared), per convention (see
/// <see cref="ConnectivityCommandStatus"/>/<see cref="MeterCommandStatus"/> for the same pattern).
/// Only <see cref="Acknowledged"/> means the meter actually switched its payment mode.
/// </summary>
public enum PaymentModeChangeStatus
{
    Queued,
    Sent,
    Acknowledged,
    Failed,
    TimedOut,
}
