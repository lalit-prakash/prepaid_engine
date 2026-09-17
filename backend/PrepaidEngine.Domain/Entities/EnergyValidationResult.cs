using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// The stored outcome of cross-checking two MDMS profile sources for one consumer/meter/day —
/// e.g. BP register delta vs DLP total (<see cref="EnergyValidationRule.BpVsDlp"/>). Computed by
/// <c>MeterDataIngestionService.EvaluateEnergyValidationAsync</c> against configurable tolerance
/// thresholds (<c>EnergyValidationOptions</c>) — never a hard-coded tolerance. Re-evaluating the
/// same consumer/meter/date/rule updates this row in place rather than inserting a duplicate
/// (unique per <c>ConsumerId + MeterId + ValidationDate + Rule</c>) — history of a corrected
/// upstream reading is not needed here; <see cref="EvaluatedAt"/> shows when it last ran.
///
/// This entity records the comparison only — it does not itself decide to hold or release
/// billing. A <c>Fail</c> here is surfaced as a candidate <c>MeterBillingControl</c> hold reason
/// by the billing pipeline, which makes that decision explicitly.
/// </summary>
public class EnergyValidationResult
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid MeterId { get; private set; }
    public DateOnly ValidationDate { get; private set; }
    public EnergyValidationRule Rule { get; private set; }
    public decimal ExpectedValueKwh { get; private set; }
    public decimal ActualValueKwh { get; private set; }
    public decimal VarianceKwh { get; private set; }
    public decimal VariancePct { get; private set; }
    public EnergyValidationStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime EvaluatedAt { get; private set; }

    public EnergyValidationResult(
        Guid id,
        Guid consumerId,
        Guid meterId,
        DateOnly validationDate,
        EnergyValidationRule rule,
        decimal expectedValueKwh,
        decimal actualValueKwh,
        EnergyValidationStatus status,
        string reason,
        DateTime evaluatedAt)
    {
        Id = id;
        ConsumerId = consumerId;
        MeterId = meterId;
        ValidationDate = validationDate;
        Rule = rule;
        ExpectedValueKwh = expectedValueKwh;
        ActualValueKwh = actualValueKwh;
        VarianceKwh = actualValueKwh - expectedValueKwh;
        VariancePct = expectedValueKwh == 0 ? 0 : Math.Round(VarianceKwh / expectedValueKwh * 100m, 2);
        Status = status;
        Reason = reason;
        EvaluatedAt = evaluatedAt;
    }

    // EF Core / serialization
    private EnergyValidationResult()
    {
    }

    /// <summary>Re-runs this same rule with fresh inputs, updating this row in place (see class
    /// doc comment for why this is an update rather than a new row).</summary>
    public void Reevaluate(decimal expectedValueKwh, decimal actualValueKwh, EnergyValidationStatus status, string reason, DateTime evaluatedAt)
    {
        ExpectedValueKwh = expectedValueKwh;
        ActualValueKwh = actualValueKwh;
        VarianceKwh = actualValueKwh - expectedValueKwh;
        VariancePct = expectedValueKwh == 0 ? 0 : Math.Round(VarianceKwh / expectedValueKwh * 100m, 2);
        Status = status;
        Reason = reason;
        EvaluatedAt = evaluatedAt;
    }
}
