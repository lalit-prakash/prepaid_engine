namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Status of a disconnect/reconnect command sent to the meter-command layer. A separate enum
/// from <see cref="MeterCommandStatus"/> (rather than reusing it) even though the shape is
/// identical — RC/DC and meter-credit commands are two independent operational concerns with
/// independent failure modes, and this project's convention (see <see cref="RechargeStatus"/>
/// vs. <see cref="MeterCommandStatus"/>) is to never let two unrelated lifecycles share one enum
/// just because their states happen to look alike today.
///
/// Mirrors the "no fake success states" rule: a command only means the meter's physical
/// connection actually changed once it reaches <see cref="Acknowledged"/> — never inferred from
/// the command merely being sent, and never from the consumer's own
/// <see cref="Entities.Consumer.ConnectionStatus"/> pending state, which only reflects intent.
/// </summary>
public enum ConnectivityCommandStatus
{
    Queued,
    Sent,
    Acknowledged,
    Failed,
    TimedOut,
}
