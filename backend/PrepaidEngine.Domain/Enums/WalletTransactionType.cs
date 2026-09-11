namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// The kind of movement recorded against a <see cref="Entities.PrepaidWallet"/> balance.
/// </summary>
public enum WalletTransactionType
{
    Recharge,
    BillDebit,
    Refund,
    Adjustment,

    /// <summary>An RMS-driven reconciliation adjustment (<see cref="Entities.ReconciliationAdjustment"/>)
    /// — kept distinct from <see cref="Adjustment"/> so the ledger always shows exactly which
    /// entries came from RMS's gap-push vs. any other manual adjustment.</summary>
    Reconciliation
}
