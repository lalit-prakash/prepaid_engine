using PrepaidEngine.Application.Connectivity;

namespace PrepaidEngine.Infrastructure.Connectivity;

/// <summary>
/// In-memory simulator of the meter-side connectivity-command layer for local development and
/// automated tests, used until (or instead of) a real adapter (STS/DLMS/COSEM/vendor API) is
/// wired up. Mirrors <c>PrepaidEngine.Infrastructure.MeterCommands.MockMeterCommandClient</c>'s
/// shape and its prefix-controlled outcomes, keyed on
/// <see cref="SendConnectivityCommandRequest.CorrelationId"/>.
///
/// <list type="bullet">
/// <item><description><c>*CONNFAIL*</c> anywhere in the correlation id — the meter rejects
/// the command (<see cref="ConnectivityCommandOutcome.Failed"/>).</description></item>
/// <item><description><c>*CONNTIMEOUT*</c> anywhere in the correlation id — no acknowledgement
/// arrives (<see cref="ConnectivityCommandOutcome.TimedOut"/>).</description></item>
/// <item><description>anything else — the meter acknowledges the command
/// (<see cref="ConnectivityCommandOutcome.Acknowledged"/>).</description></item>
/// </list>
/// </summary>
public class MockConnectivityCommandClient : IConnectivityCommandClient
{
    private const string FailMarker = "CONNFAIL";
    private const string TimeoutMarker = "CONNTIMEOUT";

    public Task<SendConnectivityCommandResult> SendConnectivityCommandAsync(SendConnectivityCommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var correlationId = request.CorrelationId ?? string.Empty;

        var result = correlationId switch
        {
            var c when c.Contains(FailMarker, StringComparison.OrdinalIgnoreCase) =>
                new SendConnectivityCommandResult(ConnectivityCommandOutcome.Failed, $"Meter rejected the {request.CommandType} command (simulated)."),
            var c when c.Contains(TimeoutMarker, StringComparison.OrdinalIgnoreCase) =>
                new SendConnectivityCommandResult(ConnectivityCommandOutcome.TimedOut, $"No acknowledgement received from the meter for the {request.CommandType} command (simulated)."),
            _ => new SendConnectivityCommandResult(ConnectivityCommandOutcome.Acknowledged, null),
        };

        return Task.FromResult(result);
    }
}
