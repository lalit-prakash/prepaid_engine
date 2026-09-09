using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A bill generated for a consumer from a consumption reading and the applicable tariff.
///
/// Carries a full, auditable charge breakdown rather than a single opaque total — matching the
/// column layout MePDCL's own reference workbook uses to explain a bill ("Individual Charge
/// Calculation" sheet: EC Gross → Rebate → EC Net → Fixed Charge → Energy Duty → FPPAS → [TMC →
/// CPMC] → Final Bill; TMC/CPMC aren't in that workbook's example rows, but are tariff-book
/// line items §4–5, so they're included here in the same position). <see cref="Amount"/> is
/// exactly that final total, computed once at construction:
/// <c>(EnergyChargeGross - PrepaidRebateAmount) + FixedCharge + ElectricityDutyAmount + FppasAmount + TmcAmount + CpmcAmount</c>.
/// </summary>
public class PrepaidBill
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid ConsumptionReadingId { get; private set; }
    public Guid TariffId { get; private set; }

    public decimal EnergyChargeGross { get; private set; }
    public decimal PrepaidRebateAmount { get; private set; }
    public decimal EnergyChargeNet => EnergyChargeGross - PrepaidRebateAmount;
    public decimal FixedCharge { get; private set; }
    public decimal ElectricityDutyAmount { get; private set; }

    /// <summary>
    /// This bill's share of a deferred FPPAS rate-change notification, if any — see
    /// <see cref="FppasCharge"/>. Can be negative (a rate decrease). Zero when no FPPAS applies
    /// to this bill.
    /// </summary>
    public decimal FppasAmount { get; private set; }

    /// <summary>The <see cref="FppasCharge"/> this bill's <see cref="FppasAmount"/> was allocated from, if any.</summary>
    public Guid? FppasChargeId { get; private set; }

    /// <summary>
    /// Transformer Maintenance Charge for this billing period — see
    /// <see cref="TransformerMaintenanceCharge"/>. Zero unless the consumer owns a transformer
    /// and has opted into MePDCL maintenance of it; this bill has no way to know that on its
    /// own, so the amount (already computed by the caller) is simply recorded here.
    /// </summary>
    public decimal TmcAmount { get; private set; }

    /// <summary>
    /// CT-PT Set Maintenance Charge for this billing period — see
    /// <see cref="CtPtMaintenanceCharge"/>. Zero unless the consumer owns a CT-PT set and has
    /// opted into MePDCL maintenance of it, same caveat as <see cref="TmcAmount"/>.
    /// </summary>
    public decimal CpmcAmount { get; private set; }

    /// <summary>The final billed amount: net energy charge + fixed charge + duty + FPPAS + TMC + CPMC.</summary>
    public decimal Amount { get; private set; }

    public decimal AmountPaid { get; private set; }
    public DateTime GeneratedAt { get; private set; }
    public BillStatus Status { get; private set; }

    public PrepaidBill(
        Guid id,
        Guid consumerId,
        Guid consumptionReadingId,
        Guid tariffId,
        decimal energyChargeGross,
        decimal prepaidRebateAmount,
        decimal fixedCharge,
        decimal electricityDutyAmount,
        DateTime generatedAt,
        decimal fppasAmount = 0m,
        Guid? fppasChargeId = null,
        decimal tmcAmount = 0m,
        decimal cpmcAmount = 0m)
    {
        if (energyChargeGross < 0)
            throw new ArgumentOutOfRangeException(nameof(energyChargeGross));
        if (prepaidRebateAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(prepaidRebateAmount));
        if (prepaidRebateAmount > energyChargeGross)
            throw new ArgumentOutOfRangeException(nameof(prepaidRebateAmount), "Rebate cannot exceed the gross energy charge.");
        if (fixedCharge < 0)
            throw new ArgumentOutOfRangeException(nameof(fixedCharge));
        if (electricityDutyAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(electricityDutyAmount));
        if (tmcAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(tmcAmount));
        if (cpmcAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(cpmcAmount));

        var amount = (energyChargeGross - prepaidRebateAmount) + fixedCharge + electricityDutyAmount + fppasAmount + tmcAmount + cpmcAmount;
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(fppasAmount), "The resulting bill amount cannot be negative; a credit note is not supported by this constructor.");

        Id = id;
        ConsumerId = consumerId;
        ConsumptionReadingId = consumptionReadingId;
        TariffId = tariffId;
        EnergyChargeGross = energyChargeGross;
        PrepaidRebateAmount = prepaidRebateAmount;
        FixedCharge = fixedCharge;
        ElectricityDutyAmount = electricityDutyAmount;
        FppasAmount = fppasAmount;
        FppasChargeId = fppasChargeId;
        TmcAmount = tmcAmount;
        CpmcAmount = cpmcAmount;
        Amount = amount;
        AmountPaid = 0m;
        GeneratedAt = generatedAt;
        Status = BillStatus.Generated;
    }

    // EF Core / serialization
    private PrepaidBill()
    {
    }

    public decimal OutstandingAmount => Math.Max(0m, Amount - AmountPaid);

    /// <summary>
    /// Applies a payment (typically a wallet debit settling this bill) and updates status.
    /// </summary>
    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (Status is BillStatus.Cancelled)
            throw new InvalidOperationException("Cannot apply payment to a cancelled bill.");

        AmountPaid += amount;
        Status = OutstandingAmount <= 0m ? BillStatus.Paid : BillStatus.PartiallyPaid;
    }

    public void MarkOverdue()
    {
        if (Status is BillStatus.Generated or BillStatus.PartiallyPaid)
            Status = BillStatus.Overdue;
    }

    public void Cancel()
    {
        if (Status == BillStatus.Paid)
            throw new InvalidOperationException("Cannot cancel a paid bill.");

        Status = BillStatus.Cancelled;
    }
}
