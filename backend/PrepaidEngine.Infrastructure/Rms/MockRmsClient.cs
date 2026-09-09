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
/// must provide — including under concurrent calls with the same key (see
/// <see cref="InitiateRechargeAsync"/>, which reserves the key atomically via
/// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,System.Func{TKey,TValue})"/>
/// rather than a separate check-then-act, so two racing callers for the same key can never
/// mint two different RMS reference ids).
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
        cancellationToken.ThrowIfCancellationRequested();

        // Informational only: used to set WasReplayed on the result actually returned. The
        // correctness guarantee (never minting two RMS reference ids for one idempotency key,
        // even under concurrent calls) comes from GetOrAdd below, not from this check.
        var wasAlreadyPresent = _resultsByIdempotencyKey.ContainsKey(request.IdempotencyKey);

        var result = _resultsByIdempotencyKey.GetOrAdd(request.IdempotencyKey, key =>
        {
            if (key.StartsWith(UnavailablePrefix, StringComparison.Ordinal))
            {
                // Deliberately not cached: an unavailable RMS never returned a response to
                // record, so a retry with the same key should be attempted again rather than
                // replayed. GetOrAdd does not store anything when the factory throws.
                throw new RmsUnavailableException("RMS did not respond (simulated outage).");
            }

            var (status, message) = key switch
            {
                var k when k.StartsWith(FailPrefix, StringComparison.Ordinal) =>
                    (RmsRechargeStatus.Failed, "Payment declined (simulated)."),
                var k when k.StartsWith(PendingPrefix, StringComparison.Ordinal) =>
                    (RmsRechargeStatus.Pending, "Payment is still being processed (simulated)."),
                _ => (RmsRechargeStatus.Success, (string?)null)
            };

            var fresh = new RmsRechargeResult(
                RmsReferenceId: $"RMS-{Guid.NewGuid():N}",
                Status: status,
                Message: message,
                WasReplayed: false);

            _resultsByReference[fresh.RmsReferenceId] = fresh;
            return fresh;
        });

        if (wasAlreadyPresent)
            result = result with { WasReplayed = true };

        return Task.FromResult(result);
    }

    public Task<RmsTransactionStatus> GetTransactionStatusAsync(string rmsReferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rmsReferenceId))
            throw new ArgumentException("RMS reference id is required.", nameof(rmsReferenceId));
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resultsByReference.TryGetValue(rmsReferenceId, out var result))
            throw new RmsTransactionNotFoundException(rmsReferenceId);

        return Task.FromResult(new RmsTransactionStatus(result.RmsReferenceId, result.Status, result.Message));
    }
}
