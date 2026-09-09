using System.Collections.Concurrent;
using PrepaidEngine.Application.Rms;

namespace PrepaidEngine.Infrastructure.Rms;

/// <summary>
/// In-memory simulator of RMS for local development and automated tests, used until (or
/// instead of) a real HTTP-based <see cref="IRmsClient"/> adapter is wired up. It is not a
/// production implementation: it holds no external state and resets whenever the process
/// restarts.
///
/// Outcome is controlled deterministically via a prefix on <see cref="RmsRechargeRequest.IdempotencyKey"/>
/// so tests can exercise each RMS behavior without randomness:
/// <list type="bullet">
/// <item><description><c>FAIL-*</c> — RMS reports a definite failure.</description></item>
/// <item><description><c>PENDING-*</c> — RMS reports the payment as still pending.</description></item>
/// <item><description><c>UNAVAILABLE-*</c> — simulates RMS being unreachable (throws <see cref="RmsUnavailableException"/>).</description></item>
/// <item><description>anything else — RMS reports success.</description></item>
/// </list>
/// Any other idempotency key behaves as a normal successful recharge. Repeating an
/// idempotency key that was already processed replays the original result instead of
/// creating a second transaction, matching the idempotency guarantee real RMS integrations
/// must provide.
/// </summary>
public class MockRmsClient : IRmsClient
{
    private const string FailPrefix = "FAIL-";
    private const string PendingPrefix = "PENDING-";
    private const string UnavailablePrefix = "UNAVAILABLE-";

    private readonly ConcurrentDictionary<string, RmsRechargeResult> _resultsByIdempotencyKey = new();
    private readonly ConcurrentDictionary<string, RmsRechargeResult> _resultsByReference = new();

    public Task<RmsRechargeResult> InitiateRechargeAsync(RmsRechargeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("Idempotency key is required.", nameof(request));
        if (request.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Recharge amount must be positive.");

        if (_resultsByIdempotencyKey.TryGetValue(request.IdempotencyKey, out var existing))
        {
            return Task.FromResult(existing with { WasReplayed = true });
        }

        if (request.IdempotencyKey.StartsWith(UnavailablePrefix, StringComparison.Ordinal))
        {
            // Deliberately not cached: an unavailable RMS never returned a response to record,
            // so a retry with the same key should be attempted again rather than replayed.
            throw new RmsUnavailableException("RMS did not respond (simulated outage).");
        }

        var (status, message) = request.IdempotencyKey switch
        {
            var key when key.StartsWith(FailPrefix, StringComparison.Ordinal) =>
                (RmsRechargeStatus.Failed, "Payment declined (simulated)."),
            var key when key.StartsWith(PendingPrefix, StringComparison.Ordinal) =>
                (RmsRechargeStatus.Pending, "Payment is still being processed (simulated)."),
            _ => (RmsRechargeStatus.Success, (string?)null)
        };

        var result = new RmsRechargeResult(
            RmsReferenceId: $"RMS-{Guid.NewGuid():N}",
            Status: status,
            Message: message,
            WasReplayed: false);

        _resultsByIdempotencyKey[request.IdempotencyKey] = result;
        _resultsByReference[result.RmsReferenceId] = result;

        return Task.FromResult(result);
    }

    public Task<RmsTransactionStatus> GetTransactionStatusAsync(string rmsReferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rmsReferenceId))
            throw new ArgumentException("RMS reference id is required.", nameof(rmsReferenceId));

        if (!_resultsByReference.TryGetValue(rmsReferenceId, out var result))
            throw new RmsTransactionNotFoundException(rmsReferenceId);

        return Task.FromResult(new RmsTransactionStatus(result.RmsReferenceId, result.Status, result.Message));
    }
}
