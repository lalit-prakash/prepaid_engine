namespace PrepaidEngine.Domain.Enums;

/// <summary>What kind of event a <see cref="Entities.MeterAssignment"/> row records.</summary>
public enum MeterAssignmentEventType
{
    Installed,
    Replaced,
    Removed,
}
