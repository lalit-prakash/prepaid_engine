namespace PrepaidEngine.Application.Rms;

/// <summary>
/// Port to RMS (the Recharge Management System) — the authoritative financial/wallet system.
/// The Prepaid Engine never accesses RMS's database directly and never treats its own state
/// as a replacement for RMS; all recharge/payment truth flows through this abstraction.
///
/// Implementations (real HTTP adapter, mock/simulator, etc.) live in the Infrastructure layer.
/// </summary>
public interface IRmsClient
{
    /// <summary>
    /// Submits a recharge request to RMS. Must be idempotent: calling this repeatedly with
    /// the same <see cref="RmsRechargeRequest.IdempotencyKey"/> must not cause RMS to process
    /// the recharge more than once, and should return the original result.
    /// </summary>
    Task<RmsRechargeResult> InitiateRechargeAsync(RmsRechargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries RMS for the current status of a previously submitted transaction, e.g. for
    /// reconciliation or when a recharge was left <see cref="RmsRechargeStatus.Pending"/>.
    /// </summary>
    Task<RmsTransactionStatus> GetTransactionStatusAsync(string rmsReferenceId, CancellationToken cancellationToken = default);
}
