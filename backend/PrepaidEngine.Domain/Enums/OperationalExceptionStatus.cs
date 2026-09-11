namespace PrepaidEngine.Domain.Enums;

/// <summary>Lifecycle of an <see cref="Entities.OperationalException"/> — deliberately just two
/// states (unlike the command lifecycles elsewhere in this project) since an exception is a
/// simple operator work-item, not a dispatched command with its own acknowledgement.</summary>
public enum OperationalExceptionStatus
{
    Open,
    Resolved,
}
