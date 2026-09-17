namespace PrepaidEngine.Domain.Enums;

/// <summary>Which two MDMS profile sources a given <see cref="Entities.EnergyValidationResult"/>
/// cross-checked for a consumer/meter/day — see the class doc comment for the comparison logic.</summary>
public enum EnergyValidationRule
{
    BpVsDlp = 0,
    LsVsDlp = 1,
    BpVsLs = 2,
}
