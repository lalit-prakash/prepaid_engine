namespace PrepaidEngine.Application.Sla;

/// <summary>
/// "Revenue & Risk Indicators" — deliberately never a fabricated monetary "revenue protected"
/// figure (this project has no real system-of-record for that). Every count here is a real,
/// currently-open condition: an unresolved operational exception, an active billing hold, an
/// unresolved meter alarm (tamper etc.), a disconnected consumer, or a failed energy-validation
/// check.
/// </summary>
public record RiskIndicatorsSummary(
    int OpenExceptions,
    int ActiveBillingHolds,
    int UnresolvedMeterAlarms,
    int DisconnectedConsumers,
    int FailedEnergyValidations);
