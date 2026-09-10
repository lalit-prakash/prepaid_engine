namespace PrepaidEngine.Application.Connectivity;

/// <summary>
/// Port to whatever actually talks to the physical/virtual meter to change its connection
/// state — an STS token generator, a DLMS/COSEM push, a meter vendor's own HTTP/MQTT API, etc.
/// No such integration exists in this project yet; only a mock/simulator implementation is
/// registered (see <c>MockConnectivityCommandClient</c> in the Infrastructure layer), mirroring
/// how <c>PrepaidEngine.Application.MeterCommands.IMeterCommandClient</c> relates to
/// <c>MockMeterCommandClient</c>.
///
/// This is a separate port from <c>IMeterCommandClient</c> on purpose: crediting a meter and
/// changing its physical connection state are two independent operational concerns dispatched
/// to (in general) different meter-side functions, and this project's convention is to never
/// let two unrelated commands share one abstraction just because their dispatch/acknowledge
/// shape looks alike.
/// </summary>
public interface IConnectivityCommandClient
{
    /// <summary>
    /// Dispatches a disconnect/reconnect command for the given consumer. Implementations
    /// should treat this as fire-and-await-acknowledgement for a single attempt — retries are
    /// the caller's responsibility (see <c>ConnectivityCommand.Retry()</c>), not this method's.
    /// </summary>
    Task<SendConnectivityCommandResult> SendConnectivityCommandAsync(SendConnectivityCommandRequest request, CancellationToken cancellationToken = default);
}
