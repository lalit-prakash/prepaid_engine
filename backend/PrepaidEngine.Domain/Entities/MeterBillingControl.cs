using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// Holds actual (real, meter-driven) billing for one consumer/meter when meter data quality is
/// unsafe to bill against. While <see cref="ActualBillingBlocked"/> is true, daily DLP billing
/// for the affected meter stops; the consumer continues through provisional billing where policy
/// permits. Raised/reactivated automatically by <c>BillingEngineService.IngestDailyLoadProfileAsync</c>
/// the moment an incoming DLP's starting reading is lower than that same meter's own previous
/// day's closing reading — the DLP pipeline's negative-consumption guard (the LS pipeline's
/// equivalent guard has been removed along with LS itself). Cleared only via the operator API
/// with a mandatory resolution note, matching this project's mandatory-reason discipline
/// elsewhere.
/// </summary>
public class MeterBillingControl
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public Guid MeterId { get; private set; }
    public bool ActualBillingBlocked { get; private set; }
    public string BlockReason { get; private set; }
    public DateTime BlockedAt { get; private set; }
    public DateTime? ClearedAt { get; private set; }

    public MeterBillingControl(Guid id, Guid consumerId, Guid meterId, string blockReason, DateTime blockedAt)
    {
        if (string.IsNullOrWhiteSpace(blockReason))
            throw new ArgumentException("A block reason is required.", nameof(blockReason));

        Id = id;
        ConsumerId = consumerId;
        MeterId = meterId;
        BlockReason = blockReason;
        BlockedAt = blockedAt;
        ActualBillingBlocked = true;
    }

    // EF Core / serialization
    private MeterBillingControl()
    {
        BlockReason = string.Empty;
    }

    /// <summary>Re-activates an existing (previously cleared) hold with a fresh reason —
    /// used when a new NegativeConsumption event recurs for a meter whose earlier hold had
    /// already been cleared.</summary>
    public void Reactivate(string blockReason, DateTime blockedAt)
    {
        if (string.IsNullOrWhiteSpace(blockReason))
            throw new ArgumentException("A block reason is required.", nameof(blockReason));

        ActualBillingBlocked = true;
        BlockReason = blockReason;
        BlockedAt = blockedAt;
        ClearedAt = null;
    }

    /// <summary>Clears the hold — see the class doc comment: the real operator-facing endpoint
    /// for this is a documented follow-up, not yet exposed. This method exists so the domain
    /// rule is testable now, ahead of that endpoint.</summary>
    public void Clear(DateTime clearedAt)
    {
        if (!ActualBillingBlocked)
            throw new InvalidOperationException("This meter billing control is not currently blocked.");

        ActualBillingBlocked = false;
        ClearedAt = clearedAt;
    }
}
