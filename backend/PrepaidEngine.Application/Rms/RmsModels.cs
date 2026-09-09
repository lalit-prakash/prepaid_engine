namespace PrepaidEngine.Application.Rms;

/// <summary>
/// Outcome of an RMS recharge request. RMS is the authoritative payment/wallet system;
/// the Prepaid Engine only reacts to what RMS reports here.
/// </summary>
public enum RmsRechargeStatus
{
    Success,
    Failed,
    Pending
}

/// <summary>
/// A recharge request sent to RMS on behalf of a consumer.
/// </summary>
/// <param name="ConsumerId">The Prepaid Engine's identifier for the consumer.</param>
/// <param name="Amount">Recharge amount.</param>
/// <param name="IdempotencyKey">
/// Caller-supplied key that must be stable across retries of the same logical recharge
/// attempt. RMS (and this mock) must return the original result for a repeated key rather
/// than processing the recharge twice.
/// </param>
/// <param name="CorrelationId">Correlation id propagated through logs/telemetry for this request.</param>
public record RmsRechargeRequest(
    Guid ConsumerId,
    decimal Amount,
    string IdempotencyKey,
    string CorrelationId);

/// <summary>
/// RMS's response to a recharge request.
/// </summary>
/// <param name="RmsReferenceId">RMS's own transaction reference for this recharge.</param>
/// <param name="Status">Outcome of the request as reported by RMS.</param>
/// <param name="Message">Human-readable detail, e.g. a failure reason.</param>
/// <param name="WasReplayed">
/// True when this result was served from a prior identical request with the same
/// <see cref="RmsRechargeRequest.IdempotencyKey"/> rather than freshly processed.
/// </param>
public record RmsRechargeResult(
    string RmsReferenceId,
    RmsRechargeStatus Status,
    string? Message,
    bool WasReplayed);

/// <summary>
/// Current status of a previously submitted RMS transaction, for polling/reconciliation.
/// </summary>
public record RmsTransactionStatus(
    string RmsReferenceId,
    RmsRechargeStatus Status,
    string? Message);
