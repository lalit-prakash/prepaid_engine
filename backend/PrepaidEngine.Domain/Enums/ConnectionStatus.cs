namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Supply connection status of a consumer's meter/service point.
/// </summary>
public enum ConnectionStatus
{
    Active,
    Disconnected,
    ReconnectionPending,
    DisconnectionPending
}
