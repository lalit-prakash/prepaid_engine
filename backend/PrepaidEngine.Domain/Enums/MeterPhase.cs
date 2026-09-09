namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Electrical phase configuration of a <see cref="Entities.SmartMeter"/>. Prepaid vend
/// (recharge) limits are commonly set per phase (single vs. three phase meters).
/// </summary>
public enum MeterPhase
{
    SinglePhase,
    ThreePhase
}
