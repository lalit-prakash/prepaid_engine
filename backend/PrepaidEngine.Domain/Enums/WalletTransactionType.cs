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
    Reconciliation,

    /// <summary>The daily prepaid charge computed directly from that day's Daily Load Profile
    /// (DLP) total kWh and the consumer's tariff — the sole driver of ongoing prepaid billing
    /// now that the hourly Load Survey (LS) pipeline has been removed (see
    /// <see cref="Entities.DailyLoadProfile"/> and <c>BillingEngineService.ProcessDailyAsync</c>).
    /// Reference is always <c>DLP:&lt;profile-id&gt;</c> (or <c>DLP-PROV:&lt;profile-id&gt;</c>
    /// for a provisional estimate posted when the day's DLP has not arrived yet).</summary>
    DailyDlpCharge,

    /// <summary>Reserved for a daily fixed/demand charge posted as its own ledger line, distinct
    /// from energy. The current DLP daily charge bundles the fixed charge into one
    /// <see cref="DailyDlpCharge"/> amount, so this type is not yet posted anywhere — kept as a
    /// named type ahead of a future breakdown, per the spec's explicit list of new transaction
    /// types, rather than invented later under a different name.</summary>
    FixedCharge,

    /// <summary>The one-time opening charge posted at postpaid→prepaid conversion, covering
    /// consumption from the 1st of the conversion month up to the conversion date (see
    /// <see cref="Entities.ConversionRequest.InitialReading"/> and the conversion endpoint).</summary>
    ConversionOpeningCharge,

    /// <summary>The FOA (Final/Fixed Obligation Amount) and DIA (Deposit/Initial Amount) RMS
    /// shares as part of a postpaid→prepaid conversion request, credited into the new prepaid
    /// wallet once the conversion completes — zero for consumers whose outstanding balance
    /// exceeded the Rs. 10,000 threshold (see <see cref="Entities.ConversionRequest"/>).</summary>
    ConversionFoaDiaCredit,
}
