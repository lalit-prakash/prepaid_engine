namespace PrepaidEngine.Domain;

/// <summary>
/// When supply may be disconnected for lack of credit. The MePDCL tariff book (FY 2026-27, section 22.4) gives prepaid consumers "consumer friendly credit
/// hours" from 4:00 PM to 11:00 AM and on official holidays: the meter keeps supplying whatever the balance, so a disconnection can only happen between
/// 11:00 AM and 4:00 PM. The hours are Meghalaya wall-clock time (India Standard Time, UTC+5:30, no daylight saving), so this converts explicitly with a fixed
/// offset rather than trusting the server's own timezone.
///
/// Official holidays are not modelled: this project has no holiday calendar, so only the daily hours are enforced. A holiday calendar is a known gap.
/// (This replaces the 9:00 AM to 2:00 PM "Happy Hours" of the AMISP integration document; where the two disagree the tariff book wins.)
/// </summary>
public static class DisconnectionWindow
{
    public const int StartHourIst = 11;
    public const int EndHourIst = 16;
    public static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);
    public const string Description = "11:00 AM to 4:00 PM IST";

    public static bool IsOpen(DateTime utcNow)
    {
        var ist = utcNow.Add(IstOffset);
        return ist.Hour >= StartHourIst && ist.Hour < EndHourIst;
    }
}
