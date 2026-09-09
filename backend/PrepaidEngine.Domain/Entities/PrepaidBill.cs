using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A bill generated for a consumer from a consumption reading and the applicable tariff.
///
/// Carries a full, auditable charge breakdown rather than a single opaque total — matching the
/// column layout MePDCL's own reference workbook uses to explain a bill ("Individual Charge
/// Calculation" sheet: EC Gross → Rebate → EC Net → Fixed Charge → Energy Duty → FPPAS → Final
/// Bill). <see cref="Amount"/> is exactly that final total, computed once at construction:
/// <c>(EnergyChargeGross - PrepaidRebateAmount) + FixedCharge + ElectricityDutyAmount + FppasAmount</c>.
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

    /// <summary>The final billed amount: net energy charge + fixed charge + duty + FPPAS.</summary>
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
        Guid? fppasChargeId = null)
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

        var amount = (energyChargeGross - prepaidRebateAmount) + fixedCharge + electricityDutyAmount + fppasAmount;
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
