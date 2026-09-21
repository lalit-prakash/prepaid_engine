namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// The prepaid bill for one consumer and one day, with every component that made up the amount debited from the wallet, so a debit can be
/// explained line by line: energy charge, the 2% rebate, the daily fixed charge, electricity duty, and the optional surcharge, maintenance
/// charges and FPPAS share. Written by the daily billing run from <see cref="DailyBillCalculator"/>; it is a record of what was charged, never edited.
/// </summary>
public class DailyBill
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid TariffId { get; private set; }
    public DateOnly BillDate { get; private set; }

    /// <summary>The wallet debit reference this bill belongs to (<c>DLP:{id}</c>, or <c>DLP-PROV:{id}</c> for an estimated day), so the two can be matched.</summary>
    public string Reference { get; private set; } = string.Empty;

    /// <summary>True when the day was billed on an estimate because no daily load profile had arrived.</summary>
    public bool IsProvisional { get; private set; }

    public decimal Kwh { get; private set; }

    /// <summary>The energy the energy charge was worked on, in the tariff's unit: kVAh for HT, EHT and Industrial LT (when the profile had it), otherwise kWh.</summary>
    public decimal BilledEnergy { get; private set; }
    public string BilledUnit { get; private set; } = "kWh";
    public decimal MonthToDateKwhBefore { get; private set; }
    public decimal GrossEnergyCharge { get; private set; }
    public decimal PrepaidRebate { get; private set; }
    public decimal FixedCharge { get; private set; }
    public decimal ElectricityDuty { get; private set; }
    public decimal LtSideMeteringSurcharge { get; private set; }
    public decimal Tmc { get; private set; }
    public decimal Cpmc { get; private set; }
    public decimal FppasShare { get; private set; }

    /// <summary>The amount debited, rounded to paise.</summary>
    public decimal Total { get; private set; }

    /// <summary>What the calculation had to assume, in plain words (for example kVAh billed on kWh); null when it assumed nothing.</summary>
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public DailyBill(Guid id, Guid consumerId, Guid tariffId, DateOnly billDate, string reference, bool isProvisional, DailyBillBreakdown b, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("A reference is required.", nameof(reference));
        Id = id;
        ConsumerId = consumerId;
        TariffId = tariffId;
        BillDate = billDate;
        Reference = reference;
        IsProvisional = isProvisional;
        Kwh = b.DayKwh;
        BilledEnergy = b.BilledEnergy;
        BilledUnit = b.BilledUnit;
        MonthToDateKwhBefore = b.MonthToDateKwhBefore;
        GrossEnergyCharge = b.GrossEnergyCharge;
        PrepaidRebate = b.PrepaidRebate;
        FixedCharge = b.FixedCharge;
        ElectricityDuty = b.ElectricityDuty;
        LtSideMeteringSurcharge = b.LtSideMeteringSurcharge;
        Tmc = b.Tmc;
        Cpmc = b.Cpmc;
        FppasShare = b.FppasShare;
        Total = b.TotalRounded;
        Notes = b.Notes.Count == 0 ? null : string.Join(" ", b.Notes);
        CreatedAt = createdAt;
    }

    // EF Core / serialization
    private DailyBill() { }
}
