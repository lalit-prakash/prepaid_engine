namespace PrepaidEngine.Domain.Enums;

/// <summary>Processing status of a <see cref="Entities.LoadSurveyInterval"/> block — see
/// <see cref="LoadSurveyQuality"/> for the separate data-quality classification.</summary>
public enum LoadSurveyStatus
{
    Received,
    Validated,
    Rejected,
    Processed,
    Provisional,
}
