using PrepaidEngine.Application.Conversion;

namespace PrepaidEngine.Infrastructure.Conversion;

/// <summary>
/// In-memory simulator of the MDMS → HES → Meter payment-mode-change relay for local development
/// and automated tests, used until (or instead of) a real HES/meter adapter is wired up. Mirrors
/// <c>MockConnectivityCommandClient</c>'s shape and its prefix-controlled outcomes, keyed on
/// <see cref="PaymentModeChangeRequest.CorrelationId"/> (in practice, the conversion request's
/// own <c>TransactionId</c>).
///
/// <list type="bullet">
/// <item><description><c>*PMCFAIL*</c> anywhere in the correlation id — the meter/HES rejects
/// the command (<see cref="PaymentModeChangeOutcome.Failed"/>).</description></item>
/// <item><description><c>*PMCTIMEOUT*</c> anywhere in the correlation id — no acknowledgement
/// arrives (<see cref="PaymentModeChangeOutcome.TimedOut"/>).</description></item>
/// <item><description>anything else — the meter acknowledges the command
/// (<see cref="PaymentModeChangeOutcome.Acknowledged"/>).</description></item>
/// </list>
///
/// On acknowledgement, this mock also has to invent the meter's current cumulative reading
/// (needed for the conversion's opening-bill calculation) since there is no real meter to ask —
/// this is the one place in this simulator that fabricates a number rather than echoing one the
/// caller supplied, and it is documented here precisely because that's a deliberate exception to
/// this project's "never fabricate a number" rule, scoped to a mock external system standing in
/// for hardware this project has no integration with.
/// </summary>
public class MockPaymentModeChangeClient : IPaymentModeChangeClient
{
    private const string FailMarker = "PMCFAIL";
    private const string TimeoutMarker = "PMCTIMEOUT";

    /// <summary>Assumed average daily consumption (kWh) this mock uses to fabricate a plausible
    /// "current reading" on acknowledgement — never presented as real metering data.</summary>
    private const decimal AssumedDailyConsumptionKwh = 4.5m;

    public Task<PaymentModeChangeResult> ChangePaymentModeAsync(
        PaymentModeChangeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var correlationId = request.CorrelationId ?? string.Empty;

        var result = correlationId switch
        {
            var c when c.Contains(FailMarker, StringComparison.OrdinalIgnoreCase) =>
                new PaymentModeChangeResult(PaymentModeChangeOutcome.Failed, null, "Meter/HES rejected the payment-mode-change command (simulated)."),
            var c when c.Contains(TimeoutMarker, StringComparison.OrdinalIgnoreCase) =>
                new PaymentModeChangeResult(PaymentModeChangeOutcome.TimedOut, null, "No acknowledgement received via HES for the payment-mode-change command (simulated)."),
            _ => new PaymentModeChangeResult(PaymentModeChangeOutcome.Acknowledged, EstimateMeterReading(request), null),
        };

        return Task.FromResult(result);
    }

    /// <summary>Fabricates a plausible current cumulative reading for a mock acknowledgement —
    /// see the class doc comment.</summary>
    private static decimal EstimateMeterReading(PaymentModeChangeRequest request)
    {
        var daysElapsed = Math.Max(0, (request.ConversionDate.Date - request.InitialReadingDateTime.Date).Days);
        return request.InitialReading + daysElapsed * AssumedDailyConsumptionKwh;
    }
}
