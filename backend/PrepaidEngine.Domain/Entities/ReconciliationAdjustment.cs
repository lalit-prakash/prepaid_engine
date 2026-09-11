namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A signed wallet adjustment pushed by RMS, per "Prepaid_Integration_Requirement_Document_
/// ProposalFromAMISP_V1.1" sections 7 & 8: RMS reconciles AMISP's daily billing data against its
/// own shadow monthly bill for the prepaid consumer, and pushes any gap it finds — or any credit
/// owed the consumer (bill revision, meter swap, etc.) — to AMISP as a signed amount. This is an
/// inbound instruction from RMS, not something AMISP computes by comparing its own internal
/// amounts (an earlier design of this entity did that three-way comparison — that direction was
/// wrong against the real spec and has been replaced).
///
/// Applying one credits/debits the consumer's wallet exactly like a recharge (see the recharge
/// endpoint), tagged with <see cref="WalletTransactionType.Reconciliation"/> so the ledger always
/// shows which entries came from a real top-up vs. an RMS-driven correction. Unlike a recharge,
/// the Rs. 500 minimum does not apply, and the amount may be negative (spec section 6).
///
/// This is a log of something already applied, not a lifecycle — the amount takes effect
/// immediately when the adjustment is created (mirrors <see cref="AuditEntry"/>'s immutability),
/// so unlike <see cref="ConversionRequest"/> or <see cref="ConnectivityCommand"/> there is no
/// separate "apply" step to call later.
/// </summary>
public class ReconciliationAdjustment
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }

    /// <summary>RMS's consumer number, echoed for traceability (see ConversionRequest.ConsumerNumber
    /// for the same AccountNumber-matching convention).</summary>
    public string ConsumerNumber { get; private set; }

    /// <summary>Signed amount: positive credits the wallet, negative debits it. Never zero — a
    /// zero adjustment is not a reconciliation event.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Always "Reconciliation" — kept as an explicit field (rather than assumed) so the
    /// wallet ledger and any export reflect exactly what RMS's payload said, per spec section 6's
    /// "Payment mode (Bill Desk, Paytm, Reconciliation etc.)".</summary>
    public string PaymentMode { get; private set; }

    /// <summary>RMS's reconciliation date for this adjustment (spec section 6: "Payment Date/
    /// Reconciliation date").</summary>
    public DateTime ReconciliationDate { get; private set; }

    /// <summary>Required free-text reference/reason RMS supplied for the gap or credit — mirrors
    /// this project's mandatory-reason discipline (see ConnectivityCommand.Reason).</summary>
    public string Reference { get; private set; }

    /// <summary>The consumer's wallet balance immediately after this adjustment was applied —
    /// captured at apply time so the historical record does not depend on replaying the ledger.</summary>
    public decimal BalanceAfter { get; private set; }

    public DateTime AppliedAt { get; private set; }

    public ReconciliationAdjustment(
        Guid id,
        Guid consumerId,
        string consumerNumber,
        decimal amount,
        DateTime reconciliationDate,
        string reference,
        decimal balanceAfter,
        DateTime appliedAt)
    {
        if (string.IsNullOrWhiteSpace(consumerNumber))
            throw new ArgumentException("A consumer number is required.", nameof(consumerNumber));
        if (amount == 0)
            throw new ArgumentException("A reconciliation adjustment amount cannot be zero.", nameof(amount));
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("A reference is required for a reconciliation adjustment.", nameof(reference));

        Id = id;
        ConsumerId = consumerId;
        ConsumerNumber = consumerNumber;
        Amount = amount;
        PaymentMode = "Reconciliation";
        ReconciliationDate = reconciliationDate;
        Reference = reference;
        BalanceAfter = balanceAfter;
        AppliedAt = appliedAt;
    }

    // EF Core / serialization
    private ReconciliationAdjustment()
    {
        ConsumerNumber = string.Empty;
        PaymentMode = "Reconciliation";
        Reference = string.Empty;
    }
}
