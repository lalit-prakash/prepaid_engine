using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Infrastructure.Persistence.Seed;

/// <summary>
/// Every tariff schedule in the MePDCL Electricity Distribution Tariff for FY 2026-27 (effective 1 April 2026), as data: energy slabs or
/// Time-of-Day rates, fixed charge and its basis, the 2% prepaid rebate, emergency credit, vend limits and initial credit (§22). The rates and
/// figures below are read straight from the tariff book's tables (A.1 LT, A.2 HT, A.3 EHT, §3 minimum charges, §22 prepaid facilities); anything
/// the book leaves open is stated on the row.
///
/// Seeded for Development (like the other demo data); a real deployment brings the same schedules in through the tariff change workflow.
/// Idempotent by tariff name: a schedule that is missing is added; one that exists but differs from the book (for example the DLT tariff seeded earlier) is replaced by the
/// book version and its consumers moved to it.
/// </summary>
public static class TariffCatalogue2026
{
    public sealed record Entry(
        string Name, string ScheduleCode, ConsumerCategory Category, VoltageLevel Voltage, EnergyUnit Unit, FixedChargeBasis Basis,
        decimal FixedCharge, TariffSlab[] Slabs, TouPeriod[]? Tou = null);

    private static TariffSlab[] Flat(decimal rate) => new[] { new TariffSlab(0, null, rate) };

    private static TouPeriod[] Tod(decimal normal, decimal peak, decimal offPeak) => new[]
    {
        new TouPeriod("Normal", new TimeSpan(6, 0, 0), new TimeSpan(17, 0, 0), normal),
        new TouPeriod("Peak", new TimeSpan(17, 0, 0), new TimeSpan(23, 0, 0), peak),
        new TouPeriod("Off-Peak", new TimeSpan(23, 0, 0), new TimeSpan(6, 0, 0), offPeak),
    };

    public static IReadOnlyList<Entry> Build() => new[]
    {
        // ---- Low Tension (fixed charge per kW per month, energy per kWh unless stated)
        new Entry("MePDCL Domestic (DLT)", "DLT", ConsumerCategory.Domestic, VoltageLevel.LT, EnergyUnit.Kwh, FixedChargeBasis.PerKw, 90m,
            new[] { new TariffSlab(0, 100, 5.00m), new TariffSlab(100, 200, 5.04m), new TariffSlab(200, null, 5.10m) }),
        new Entry("MePDCL Non-Domestic (CLT)", "CLT", ConsumerCategory.NonDomestic, VoltageLevel.LT, EnergyUnit.Kwh, FixedChargeBasis.PerKw, 170m, Flat(7.45m)),
        new Entry("MePDCL General Purpose (GP)", "GP", ConsumerCategory.GeneralPurpose, VoltageLevel.LT, EnergyUnit.Kwh, FixedChargeBasis.PerKw, 180m, Flat(7.35m)),
        new Entry("MePDCL Public Water Supply / STP (WSLT)", "WSLT", ConsumerCategory.PublicWaterSupply, VoltageLevel.LT, EnergyUnit.Kwh, FixedChargeBasis.PerKw, 180m, Flat(7.00m)),
        new Entry("MePDCL Public Lighting, metered (PL)", "PL", ConsumerCategory.PublicLighting, VoltageLevel.LT, EnergyUnit.Kwh, FixedChargeBasis.PerKw, 180m, Flat(7.00m)),
        new Entry("MePDCL EV Charging Station LT (EVLT)", "EVLT", ConsumerCategory.ElectricVehicle, VoltageLevel.LT, EnergyUnit.Kwh, FixedChargeBasis.None, 0m, Flat(6.00m)),
        new Entry("MePDCL Crematorium (CRM)", "CRM", ConsumerCategory.Crematorium, VoltageLevel.LT, EnergyUnit.Kwh, FixedChargeBasis.PerKw, 55m, Flat(4.75m)),
        new Entry("MePDCL Agriculture (AP)", "AP", ConsumerCategory.Agriculture, VoltageLevel.LT, EnergyUnit.Kwh, FixedChargeBasis.PerKwOrHp, 130m, Flat(3.15m)),
        new Entry("MePDCL Industrial LT (ILT)", "ILT", ConsumerCategory.Industrial, VoltageLevel.LT, EnergyUnit.Kvah, FixedChargeBasis.PerKva, 170m, Flat(6.80m)),
        // Kutir Jyoti / BPL, metered: Rs. 4.57 up to 30 kWh a month, then the ordinary domestic slab for the units above (the book's "excess units on the
        // appropriate slab for normal domestic consumers"), which keeps the domestic 100 / 200 boundaries. The book names no fixed charge for it.
        new Entry("MePDCL Kutir Jyoti / BPL, metered (KJM)", "KJM", ConsumerCategory.KutirJyotiBpl, VoltageLevel.LT, EnergyUnit.Kwh, FixedChargeBasis.None, 0m,
            new[] { new TariffSlab(0, 30, 4.57m), new TariffSlab(30, 100, 5.00m), new TariffSlab(100, 200, 5.04m), new TariffSlab(200, null, 5.10m) }),

        // ---- High Tension, 11 kV (fixed charge per kVA per month, energy per kVAh unless stated)
        new Entry("MePDCL Domestic HT (DHT)", "DHT", ConsumerCategory.Domestic, VoltageLevel.HT, EnergyUnit.Kvah, FixedChargeBasis.PerKva, 350m, Flat(5.85m)),
        new Entry("MePDCL Commercial HT (CHT)", "CHT", ConsumerCategory.NonDomestic, VoltageLevel.HT, EnergyUnit.Kvah, FixedChargeBasis.PerKva, 390m, Flat(6.00m)),
        new Entry("MePDCL General Purpose Bulk Supply (BS)", "BS", ConsumerCategory.GeneralPurpose, VoltageLevel.HT, EnergyUnit.Kvah, FixedChargeBasis.PerKva, 420m, Flat(6.10m)),
        new Entry("MePDCL Public Water Supply / STP HT (WSHT)", "WSHT", ConsumerCategory.PublicWaterSupply, VoltageLevel.HT, EnergyUnit.Kvah, FixedChargeBasis.PerKva, 410m, Flat(6.60m)),
        new Entry("MePDCL Industrial HT (IHT)", "IHT", ConsumerCategory.Industrial, VoltageLevel.HT, EnergyUnit.Kvah, FixedChargeBasis.PerKva, 340m, Array.Empty<TariffSlab>(), Tod(5.55m, 6.66m, 4.72m)),
        new Entry("MePDCL Ferro Alloy HT (FAHT)", "FAHT", ConsumerCategory.FerroAlloy, VoltageLevel.HT, EnergyUnit.Kvah, FixedChargeBasis.PerKva, 500m, Flat(5.55m)),
        new Entry("MePDCL EV Charging Station HT (EVHT)", "EVHT", ConsumerCategory.ElectricVehicle, VoltageLevel.HT, EnergyUnit.Kwh, FixedChargeBasis.None, 0m, Flat(6.00m)),

        // ---- Extra High Tension, 33 kV and above
        new Entry("MePDCL Industrial EHT (IEHT)", "IEHT", ConsumerCategory.Industrial, VoltageLevel.EHT, EnergyUnit.Kvah, FixedChargeBasis.PerKva, 500m, Array.Empty<TariffSlab>(), Tod(6.60m, 7.92m, 5.61m)),
        new Entry("MePDCL Ferro Alloy EHT (FAEHT)", "FAEHT", ConsumerCategory.FerroAlloy, VoltageLevel.EHT, EnergyUnit.Kvah, FixedChargeBasis.PerKva, 500m, Flat(5.60m)),
    };

    /// <summary>Prepaid meter facilities that depend on the category (§22.5-22.7): General Purpose has higher emergency credit, vend limits and initial credit.</summary>
    public static (decimal EmergencyCredit, decimal MaxVendSinglePhase, decimal MaxVendThreePhase, decimal InitialCreditSinglePhase, decimal InitialCreditThreePhase)
        PrepaidFacilities(ConsumerCategory category) => category == ConsumerCategory.GeneralPurpose
            ? (TariffBookParameters.EmergencyCreditGeneralPurpose, 50_000m, 100_000m, 1_000m, 5_000m)
            : (TariffBookParameters.EmergencyCreditOthers, 15_000m, 25_000m, 100m, 200m);

    public static Tariff ToTariff(Entry e)
    {
        var f = PrepaidFacilities(e.Category);
        // HT fixed charge is never billed on less than 56 kVA (§3.2); the EHT and LT schedules state no such floor.
        decimal? minDemand = e.Voltage == VoltageLevel.HT && e.Basis == FixedChargeBasis.PerKva ? TariffBookParameters.HtMinimumChargeableKva : null;
        // Minimum recharge: the book prints ₹500 only beside the General Purpose row (§22.6), so it is a General Purpose limit; the others have none.
        decimal? minVend = e.Category == ConsumerCategory.GeneralPurpose ? TariffBookParameters.MinimumVendAmountGeneralPurpose : null;
        return new Tariff(
            Guid.NewGuid(), e.Name, e.Category, e.Slabs,
            fixedChargePerUnitPerMonth: e.FixedCharge,
            prepaidEnergyRebatePercent: TariffBookParameters.PrepaidEnergyRebatePercent,
            emergencyCreditLimit: f.EmergencyCredit,
            minVendAmountSinglePhase: minVend, maxVendAmountSinglePhase: f.MaxVendSinglePhase,
            minVendAmountThreePhase: minVend, maxVendAmountThreePhase: f.MaxVendThreePhase,
            touPeriods: e.Tou,
            scheduleCode: e.ScheduleCode, voltageLevel: e.Voltage, energyUnit: e.Unit, fixedChargeBasis: e.Basis,
            minimumChargeableDemand: minDemand,
            initialCreditSinglePhase: f.InitialCreditSinglePhase, initialCreditThreePhase: f.InitialCreditThreePhase);
    }

    /// <summary>Adds every schedule that is not there yet (matched by name) and fills in the classification of ones created before it existed.</summary>
    public static async Task SeedAsync(PrepaidEngineDbContext db, CancellationToken cancellationToken = default)
    {
        var existing = await db.Tariffs.Where(t => t.Status == TariffLifecycleStatus.Active).Select(t => t.Name).ToListAsync(cancellationToken);
        var names = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Build().Where(e => !names.Contains(e.Name)))
            db.Tariffs.Add(ToTariff(e));

        // The DLT tariff seeded before schedule codes existed: same rates, add its classification and the §22.7 initial credit.
        var legacy = await db.Tariffs.Where(t => t.Name == "MePDCL Domestic (DLT)" && t.ScheduleCode == null).ToListAsync(cancellationToken);
        var dlt = ToTariff(Build()[0]);
        foreach (var t in legacy) t.InheritClassification(dlt);

        await db.SaveChangesAsync(cancellationToken);

        // Demo tariffs seeded before the book's figures were settled (the DLT tariff's 5.70 / 5.90 slabs, a Rs. 500 minimum recharge on every schedule) are brought to the book:
        // each is replaced by a book version and its consumers move to it. A real deployment does this through the tariff change workflow instead.
        var active = await db.Tariffs.Include(t => t.Slabs).Include(t => t.TouPeriods).Where(t => t.Status == TariffLifecycleStatus.Active).ToListAsync(cancellationToken);
        foreach (var row in TariffBookCheck.Run(active).Where(r => r.Status == "Differs"))
        {
            var old = active.Single(t => t.Id == row.TariffId);
            var entry = Build().Single(e => e.ScheduleCode == row.ScheduleCode);
            old.Retire();
            await db.SaveChangesAsync(cancellationToken);
            var replacement = ToTariff(entry);
            db.Tariffs.Add(replacement);
            await db.SaveChangesAsync(cancellationToken);
            await db.Consumers.Where(c => c.TariffId == old.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.TariffId, replacement.Id), cancellationToken);
        }
    }
}
