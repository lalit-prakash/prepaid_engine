namespace PrepaidEngine.Domain.Enums;

/// <summary>Informational meter events — logged for history/analytics, never requiring
/// operator acknowledgement (contrast <see cref="MeterAlarmCode"/>, which does).</summary>
public enum MeterEventCode
{
    PowerFailure = 0,
    PowerRestoration = 1,
    CommunicationFailure = 2,
    CommunicationRestoration = 3,
    RelayClosed = 4,
    RelayOpened = 5,
    MeterClockChanged = 6,
    Other = 99,
}
