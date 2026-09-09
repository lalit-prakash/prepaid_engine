namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Wiring configuration of a consumer-owned CT-PT (Current Transformer - Potential Transformer)
/// metering set — determines the CT-PT Set Maintenance Charge rate (tariff book §4).
/// </summary>
public enum CtPtWiring
{
    ThreePhaseThreeWire,
    ThreePhaseFourWire
}
