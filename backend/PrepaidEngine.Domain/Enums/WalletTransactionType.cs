namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// The kind of movement recorded against a <see cref="Entities.PrepaidWallet"/> balance.
/// </summary>
public enum WalletTransactionType
{
    Recharge,
    BillDebit,
    Refund,
    Adjustment
}
