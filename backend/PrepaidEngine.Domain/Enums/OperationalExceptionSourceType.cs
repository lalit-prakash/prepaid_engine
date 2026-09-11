namespace PrepaidEngine.Domain.Enums;

/// <summary>What kind of real-world event produced an <see cref="Entities.OperationalException"/>.
/// <see cref="Entities.OperationalException.SourceId"/> is the id of the record of this type.</summary>
public enum OperationalExceptionSourceType
{
    MeterCommand,
    ConnectivityCommand,
}
