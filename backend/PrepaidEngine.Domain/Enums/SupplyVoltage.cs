namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Supply voltage level at which HT/EHT equipment (transformer, CT-PT set) is connected —
/// determines the applicable Transformer Maintenance Charge and CT-PT Set Maintenance Charge
/// rates (tariff book §4–5).
/// </summary>
public enum SupplyVoltage
{
    Kv11,
    Kv33,
    Kv132
}
