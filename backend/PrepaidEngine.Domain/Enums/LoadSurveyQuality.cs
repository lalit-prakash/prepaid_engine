namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Data-quality classification of a <see cref="Entities.LoadSurveyInterval"/> block, per the
/// LS/DLP billing pipeline spec. Deliberately separate from
/// <see cref="LoadSurveyStatus"/> (processing status) — a block can be Quality=NegativeConsumption
/// and Status=Rejected, or Quality=Valid and Status=Processed; conflating the two would lose the
/// distinction between "what's wrong with the data" and "what the engine has done about it".
/// </summary>
public enum LoadSurveyQuality
{
    Valid,
    Duplicate,
    Missing,
    NegativeConsumption,
    MeterReset,
    Rollover,
    OutOfSequence,
    Estimated,
}
