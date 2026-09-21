namespace PrepaidEngine.Domain.Enums;

/// <summary>Supply voltage class of a tariff schedule (tariff book, "Classification of Supply").</summary>
public enum VoltageLevel
{
    /// <summary>Low Tension: single phase 230 V up to 5 kW, three phase 400 V above 5 kW up to 50 kW.</summary>
    LT,

    /// <summary>High Tension, 11 kV: connected load above 50 kW up to 2000 kW.</summary>
    HT,

    /// <summary>Extra High Tension, 33 kV and above: connected load above 2000 kW.</summary>
    EHT,
}

/// <summary>The unit a tariff's energy charge is quoted in: kWh for most LT schedules, kVAh for HT/EHT and Industrial LT.</summary>
public enum EnergyUnit
{
    Kwh,
    Kvah,
}

/// <summary>What a tariff's fixed (minimum) charge is charged per, each month.</summary>
public enum FixedChargeBasis
{
    /// <summary>Per kW of connected load.</summary>
    PerKw,

    /// <summary>Per kVA of contract demand or connected load.</summary>
    PerKva,

    /// <summary>Per kW or per HP of connected load (Agriculture; 1 HP = 0.746 kW).</summary>
    PerKwOrHp,

    /// <summary>No fixed charge (Electric Vehicle charging, Kutir Jyoti).</summary>
    None,
}
