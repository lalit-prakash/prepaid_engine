using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Domain;

/// <summary>One load survey interval as the Time-of-Day split needs it: when it started (UTC) and the energy in it.</summary>
public readonly record struct SurveyInterval(DateTime StartUtc, decimal Kwh, decimal? Kvah);

/// <summary>The day's energy by Time-of-Day band, and anything worth telling the reader about how it was worked out.</summary>
public sealed record TimeOfDayBands(IReadOnlyDictionary<string, decimal> ByBand, string? Note);

/// <summary>
/// Splits a day's energy into a Time-of-Day tariff's bands (Normal, Peak, Off-Peak) from the load survey intervals MDM sends every 15 or 30 minutes. Each interval
/// goes to the band its start time falls in, by India Standard Time clock (the bands are Meghalaya wall-clock).
///
/// When the tariff is billed on kVAh and every interval carries kVAh, the bands hold kVAh exactly. When some intervals have no kVAh, the intervals' kWh is used only
/// for the shares (each band's share of the day's kWh) and those shares are applied to the day's billed energy, and the note says so. When intervals do not cover
/// the whole day the missing energy is left for the calculator to price at the Normal rate.
/// </summary>
public static class TimeOfDaySplit
{
    /// <summary>Returns null when there are no intervals (the calculator then prices the whole day at the Normal rate and says so).</summary>
    public static TimeOfDayBands? Split(Tariff tariff, IReadOnlyCollection<SurveyInterval> intervals, decimal billedDayEnergy, bool billedInKvah)
    {
        if (intervals.Count == 0 || tariff.TouPeriods.Count == 0) return null;

        string BandOf(SurveyInterval i) => tariff.ClassifyTimeOfDay((i.StartUtc + DisconnectionWindow.IstOffset).TimeOfDay);
        var exact = billedInKvah ? intervals.All(i => i.Kvah.HasValue) : true;

        if (exact)
        {
            var bands = intervals.GroupBy(BandOf).ToDictionary(g => g.Key, g => g.Sum(i => billedInKvah ? i.Kvah!.Value : i.Kwh));
            return new TimeOfDayBands(bands, null);
        }

        // kVAh is what is billed but the intervals only have kWh: use the kWh shares of each band against the day's billed energy.
        var kwhByBand = intervals.GroupBy(BandOf).ToDictionary(g => g.Key, g => g.Sum(i => i.Kwh));
        var totalKwh = kwhByBand.Values.Sum();
        if (totalKwh <= 0) return null;
        var scaled = kwhByBand.ToDictionary(kv => kv.Key, kv => billedDayEnergy * kv.Value / totalKwh);
        return new TimeOfDayBands(scaled, "The load survey intervals carry kWh but no kVAh, so each Time-of-Day band's share of the day's kWh was applied to the day's kVAh.");
    }
}
