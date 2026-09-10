namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Status of a meter credit command sent to the meter-command layer (an abstraction over
/// whatever protocol actually talks to the meter — STS token, DLMS/COSEM push, or a meter
/// vendor's own API — none of which this project integrates with yet; see
/// <see cref="PrepaidEngine.Domain.Entities.MeterCommand"/>'s doc comment).
///
/// Deliberately mirrors the UI/UX request's "no fake success states" rule: a command is never
/// considered done until <see cref="Acknowledged"/> is reached from a real downstream
/// acknowledgement — RMS confirming a payment (see <see cref="RechargeStatus"/>) must never be
/// conflated with the meter itself having been credited.
/// </summary>
public enum MeterCommandStatus
{
    Queued,
    Sent,
    Acknowledged,
    Failed,
    TimedOut,
}
