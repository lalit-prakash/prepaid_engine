namespace PrepaidEngine.Domain.Enums;

/// <summary>The meter's own relay contact state at the moment an Instantaneous (IP) reading was
/// captured — independent of <see cref="ConnectionStatus"/>, which is this engine's own
/// commanded/expected state for the consumer.</summary>
public enum MeterRelayStatus
{
    Closed = 0,
    Open = 1,
}
