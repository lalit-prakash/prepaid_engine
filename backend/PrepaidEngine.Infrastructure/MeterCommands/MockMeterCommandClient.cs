using PrepaidEngine.Application.MeterCommands;

namespace PrepaidEngine.Infrastructure.MeterCommands;

/// <summary>
/// In-memory simulator of the meter-command layer for local development and automated tests,
/// used until (or instead of) a real adapter (STS/DLMS/COSEM/vendor API) is wired up. Mirrors
/// <c>PrepaidEngine.Infrastructure.Rms.MockRmsClient</c>'s shape and its prefix-controlled
/// outcomes, but on <see cref="SendCreditCommandRequest.CorrelationId"/> rather than an
/// idempotency key, since dispatching a command has no idempotency contract of its own here —
/// that guarantee already lives one level up, on the recharge that triggers the dispatch.
///
/// <list type="bullet">
/// <item><description><c>*METERFAIL*</c> anywhere in the correlation id — the meter rejects
/// the command (<see cref="MeterCommandOutcome.Failed"/>).</description></item>
/// <item><description><c>*METERTIMEOUT*</c> anywhere in the correlation id — no acknowledgement
/// arrives (<see cref="MeterCommandOutcome.TimedOut"/>).</description></item>
/// <item><description>anything else — the meter acknowledges the credit
/// (<see cref="MeterCommandOutcome.Acknowledged"/>).</description></item>
/// </list>
/// </summary>
public class MockMeterCommandClient : IMeterCommandClient
{
    private const string FailMarker = "METERFAIL";
    private const string TimeoutMarker = "METERTIMEOUT";

    public Task<SendCreditCommandResult> SendCreditCommandAsync(SendCreditCommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Credit amount must be positive.");
        cancellationToken.ThrowIfCancellationRequested();

        var correlationId = request.CorrelationId ?? string.Empty;

        var externalCommandId = $"MOCK-MDM-{Guid.NewGuid():N}";
        var result = correlationId switch
        {
            var c when c.Contains(FailMarker, StringComparison.OrdinalIgnoreCase) =>
                new SendCreditCommandResult(MeterCommandOutcome.Failed, "Meter rejected the credit command (simulated).", externalCommandId, "MDM_REJECTED"),
            var c when c.Contains(TimeoutMarker, StringComparison.OrdinalIgnoreCase) =>
                new SendCreditCommandResult(MeterCommandOutcome.TimedOut, "No acknowledgement received from the meter (simulated).", externalCommandId, "MDM_NO_ACK"),
            _ => new SendCreditCommandResult(MeterCommandOutcome.Acknowledged, null, externalCommandId, "MDM_ACK_OK"),
        };

        return Task.FromResult(result);
    }
}
