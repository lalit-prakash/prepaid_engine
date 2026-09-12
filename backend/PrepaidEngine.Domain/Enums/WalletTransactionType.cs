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

    /// <summary>An hourly energy debit posted from a validated <see cref="Entities.LoadSurveyInterval"/>
    /// block (LS/DLP billing pipeline). Reference is always <c>LS:&lt;interval-id&gt;</c>.</summary>
    LoadSurveyHourlyCharge,

    /// <summary>Either the signed daily settlement (authoritative DLP daily charge minus the
    /// day's <see cref="LoadSurveyHourlyCharge"/> debits and any provisional debit already
    /// posted — never a blind re-debit of the full daily amount), or a provisional daily debit
    /// posted when the DLP is missing/invalid at the billing boundary. The reference prefix
    /// (<c>DLP-SETTLE:</c> vs. <c>DLP-PROV:</c>) distinguishes which.</summary>
    DlpSettlementAdjustment,

    /// <summary>Reserved for a daily fixed/demand charge posted as its own ledger line, distinct
    /// from energy. The current LS/DLP settlement bundles the fixed charge into one
    /// <see cref="DlpSettlementAdjustment"/> amount (per spec's own daily-charge formula), so this
    /// type is not yet posted anywhere — kept as a named type ahead of a future breakdown, per
    /// the spec's explicit list of new transaction types, rather than invented later under a
    /// different name.</summary>
    FixedCharge
}
