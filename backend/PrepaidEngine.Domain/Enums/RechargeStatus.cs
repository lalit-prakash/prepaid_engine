namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Status of a recharge processed through RMS.
/// </summary>
public enum RechargeStatus
{
    Initiated,
    Success,
    Failed,
    Reversed
}
