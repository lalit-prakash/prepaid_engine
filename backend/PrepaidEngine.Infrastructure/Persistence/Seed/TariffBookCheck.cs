using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Seed;

/// <summary>One thing an active tariff differs on from the tariff book.</summary>
public sealed record BookDifference(string Field, string Book, string Current);

/// <summary>How one book schedule compares with the tariff in force for it.</summary>
public sealed record BookCheckRow(string ScheduleCode, string Name, Guid? TariffId, string Status, IReadOnlyList<BookDifference> Differences);

/// <summary>
/// Compares the tariffs in force with the FY 2026-27 tariff book, schedule by schedule: rates and slabs, fixed charge, rebate, emergency credit,
/// vend limits, initial credit and Time-of-Day rates. It only reports; a tariff is only ever changed through the tariff change workflow. A schedule
/// with no tariff in force is reported Missing. Tariffs a utility sets above or below the book on purpose show as Differs, which is the point.
/// </summary>
public static class TariffBookCheck
{
    private static string M(decimal? v) => v.HasValue ? v.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : "not set";

    public static IReadOnlyList<BookDifference> Compare(Tariff current, Tariff book)
    {
        var diffs = new List<BookDifference>();
        void Check<T>(string field, T bookValue, T currentValue, Func<T, string> show)
        {
            if (!EqualityComparer<T>.Default.Equals(bookValue, currentValue)) diffs.Add(new BookDifference(field, show(bookValue), show(currentValue)));
        }
        Check("Fixed charge", book.FixedChargePerUnitPerMonth, current.FixedChargePerUnitPerMonth, v => M(v));
        Check("Prepaid rebate %", book.PrepaidEnergyRebatePercent, current.PrepaidEnergyRebatePercent, v => M(v));
        Check("Emergency credit", book.EmergencyCreditLimit, current.EmergencyCreditLimit, v => M(v));
        Check("Min recharge, single phase", book.MinVendAmountSinglePhase, current.MinVendAmountSinglePhase, M);
        Check("Max recharge, single phase", book.MaxVendAmountSinglePhase, current.MaxVendAmountSinglePhase, M);
        Check("Min recharge, three phase", book.MinVendAmountThreePhase, current.MinVendAmountThreePhase, M);
        Check("Max recharge, three phase", book.MaxVendAmountThreePhase, current.MaxVendAmountThreePhase, M);
        Check("Initial credit, single phase", book.InitialCreditSinglePhase, current.InitialCreditSinglePhase, M);
        Check("Initial credit, three phase", book.InitialCreditThreePhase, current.InitialCreditThreePhase, M);
        Check("Energy unit", book.EnergyUnit, current.EnergyUnit, v => v.ToString());
        Check("Voltage", book.VoltageLevel, current.VoltageLevel, v => v.ToString());

        string Slabs(Tariff t) => string.Join("; ", t.Slabs.OrderBy(s => s.FromKwh).Select(s => $"{M(s.FromKwh)}-{(s.UpToKwh.HasValue ? M(s.UpToKwh) : "above")} @ {M(s.RatePerKwh)}"));
        if (Slabs(book) != Slabs(current)) diffs.Add(new BookDifference("Energy slabs", Slabs(book) is { Length: > 0 } b ? b : "none", Slabs(current) is { Length: > 0 } c ? c : "none"));

        string Tou(Tariff t) => string.Join("; ", t.TouPeriods.OrderBy(p => p.StartTime).Select(p => $"{p.Label} {p.StartTime:hh\\:mm}-{p.EndTime:hh\\:mm} @ {M(p.RatePerKvah)}"));
        if (Tou(book) != Tou(current)) diffs.Add(new BookDifference("Time-of-Day rates", Tou(book) is { Length: > 0 } b2 ? b2 : "none", Tou(current) is { Length: > 0 } c2 ? c2 : "none"));
        return diffs;
    }

    /// <summary>Compares every book schedule with the active tariff carrying its schedule code (or, for a tariff created before codes existed, its name).</summary>
    public static IReadOnlyList<BookCheckRow> Run(IReadOnlyCollection<Tariff> activeTariffs)
    {
        var rows = new List<BookCheckRow>();
        foreach (var entry in TariffCatalogue2026.Build())
        {
            var book = TariffCatalogue2026.ToTariff(entry);
            var current = activeTariffs.FirstOrDefault(t => t.ScheduleCode == entry.ScheduleCode)
                ?? activeTariffs.FirstOrDefault(t => t.ScheduleCode == null && string.Equals(t.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            if (current is null) { rows.Add(new BookCheckRow(entry.ScheduleCode, entry.Name, null, "Missing", Array.Empty<BookDifference>())); continue; }
            var diffs = Compare(current, book);
            rows.Add(new BookCheckRow(entry.ScheduleCode, entry.Name, current.Id, diffs.Count == 0 ? "Matches" : "Differs", diffs));
        }
        return rows;
    }
}
