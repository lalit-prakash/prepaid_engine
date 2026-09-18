namespace PrepaidEngine.Domain.Enums;

/// <summary>What kind of real-world event produced an <see cref="Entities.OperationalException"/>.
/// <see cref="Entities.OperationalException.SourceId"/> is the id of the record of this type.</summary>
public enum OperationalExceptionSourceType
{
    MeterCommand,
    ConnectivityCommand,

    /// <summary>Raised automatically when an <see cref="Entities.EnergyValidationResult"/>
    /// evaluates to <see cref="EnergyValidationStatus.Fail"/> — see
    /// <c>MeterDataIngestionService.EvaluateEnergyValidationAsync</c>.</summary>
    EnergyValidation,
}
