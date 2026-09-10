using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Application.Connectivity;

/// <summary>
/// Outcome of dispatching a disconnect/reconnect command to the meter-command layer.
/// Deliberately has no "Success" value that skips a real acknowledgement — see
/// <see cref="IConnectivityCommandClient"/>.
/// </summary>
public enum ConnectivityCommandOutcome
{
    Acknowledged,
    Failed,
    TimedOut,
}

/// <summary>
/// A request to change a consumer's physical connection state, dispatched only after the
/// caller has already decided the change is warranted (e.g. an operator disconnecting for
/// negative credit, or the engine reconnecting after a qualifying recharge).
/// </summary>
/// <param name="ConsumerId">The Prepaid Engine's identifier for the consumer.</param>
/// <param name="CommandType">Which direction — never inferred, always explicit.</param>
/// <param name="CorrelationId">Correlation id propagated through logs/telemetry for this dispatch.</param>
public record SendConnectivityCommandRequest(
    Guid ConsumerId,
    ConnectivityCommandType CommandType,
    string CorrelationId);

/// <summary>
/// The meter-command layer's response to a dispatched disconnect/reconnect command.
/// </summary>
/// <param name="Outcome">What actually happened, per <see cref="ConnectivityCommandOutcome"/>.</param>
/// <param name="Message">Human-readable detail, e.g. a failure or timeout reason.</param>
public record SendConnectivityCommandResult(
    ConnectivityCommandOutcome Outcome,
    string? Message);
