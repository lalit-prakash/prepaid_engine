namespace PrepaidEngine.Application.MeterCommands;

/// <summary>
/// Port to whatever actually talks to the physical/virtual meter — an STS token generator,
/// a DLMS/COSEM push, a meter vendor's own HTTP/MQTT API, etc. No such integration exists in
/// this project yet; only a mock/simulator implementation is registered (see
/// <c>MockMeterCommandClient</c> in the Infrastructure layer), mirroring how
/// <c>PrepaidEngine.Application.Rms.IRmsClient</c> relates to <c>MockRmsClient</c>.
///
/// This is a separate port from <c>IRmsClient</c> on purpose: RMS confirming a recharge payment
/// and the meter itself being credited are two independent systems, and a single "did the
/// recharge work" abstraction would make it easy to accidentally conflate their outcomes — the
/// exact mistake the "no fake success states" rule this project follows exists to prevent.
/// </summary>
public interface IMeterCommandClient
{
    /// <summary>
    /// Dispatches a credit command for the given consumer/amount. Implementations should treat
    /// this as fire-and-await-acknowledgement for a single attempt — retries are the caller's
    /// responsibility (see <c>MeterCommand.Retry()</c>), not this method's.
    /// </summary>
    Task<SendCreditCommandResult> SendCreditCommandAsync(SendCreditCommandRequest request, CancellationToken cancellationToken = default);
}
