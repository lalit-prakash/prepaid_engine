namespace PrepaidEngine.Domain.Enums;

/// <summary>Lifecycle of a Billing Profile (BP) register reading — see <see cref="Entities.RegisterReading"/>.</summary>
public enum RegisterReadingStatus
{
    Received = 0,
    Validated = 1,
    Rejected = 2,
}
