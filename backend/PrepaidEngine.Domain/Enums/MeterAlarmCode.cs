namespace PrepaidEngine.Domain.Enums;

/// <summary>Alarm-worthy meter conditions — always requires an operator acknowledgement/resolution
/// workflow (see <see cref="Entities.MeterAlarm"/>), unlike a plain <see cref="MeterEventCode"/>.</summary>
public enum MeterAlarmCode
{
    Tamper = 0,
    MagneticInfluence = 1,
    CoverOpen = 2,
    ReverseEnergy = 3,
    VoltageAbnormality = 4,
    CurrentAbnormality = 5,
    Other = 99,
}
