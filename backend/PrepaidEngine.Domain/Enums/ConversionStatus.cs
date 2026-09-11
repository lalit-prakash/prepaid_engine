namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.ConversionRequest"/>. A separate enum from every other
/// lifecycle in this project (never shared), per convention. Only <see cref="Completed"/> means
/// the consumer's <see cref="BillingMode"/> actually changed — approval alone is a decision, not
/// an executed change, matching the "no fake success states" rule used throughout this project.
/// </summary>
public enum ConversionStatus
{
    Requested,
    Approved,
    Rejected,
    Completed,
}
