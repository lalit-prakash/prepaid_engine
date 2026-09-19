using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// An operational record of one batch billing run. Daily DLP billing runs in two stages per
/// calendar date, each tracked as its own <see cref="BillingRun"/> row — unique per
/// <c>RunType + BillingDate</c>, so a stage cannot be accidentally run twice for the same date,
/// and both stages can coexist for the same date without colliding.
/// </summary>
public class BillingRun
{
    /// <summary>Historical run type from before the two-stage split — no longer created, kept so
    /// existing rows still deserialize.</summary>
    public const string DlpDailyRunType = "DLP_DAILY";

    /// <summary>8:30-9:30 AM: bills every consumer whose DLP was received by 8:00 AM.</summary>
    public const string DlpStage1RunType = "DLP_STAGE1";

    /// <summary>12:30-1:30 PM: bills every consumer whose DLP arrived between 8:00 AM and
    /// 12:00 PM, plus a provisional charge for every consumer with no DLP by then.</summary>
    public const string DlpStage2RunType = "DLP_STAGE2";

    public Guid Id { get; private set; }
    public string RunType { get; private set; }
    public DateOnly BillingDate { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public BillingRunStatus Status { get; private set; }
    public int ConsumerCount { get; private set; }
    public int ExceptionCount { get; private set; }

    /// <summary>Last time the instance running this billing run reported progress. A run whose heartbeat is older
    /// than the lease can be taken over by another instance.</summary>
    public DateTime LastHeartbeatAt { get; private set; }

    /// <summary>Highest consumer id already processed and committed; a resumed run continues after it.</summary>
    public Guid? ResumeAfterConsumerId { get; private set; }

    public BillingRun(Guid id, string runType, DateOnly billingDate, DateTime startedAt)
    {
        if (string.IsNullOrWhiteSpace(runType))
            throw new ArgumentException("A run type is required.", nameof(runType));

        Id = id;
        RunType = runType;
        BillingDate = billingDate;
        StartedAt = startedAt;
        LastHeartbeatAt = startedAt;
        Status = BillingRunStatus.Running;
    }

    // EF Core / serialization
    private BillingRun()
    {
        RunType = string.Empty;
    }

    /// <summary>Records one committed batch: how many consumers it covered, how many raised exceptions, the last
    /// consumer id reached (the resume cursor) and a fresh heartbeat.</summary>
    public void RecordBatch(int consumers, int exceptions, Guid lastConsumerId, DateTime now)
    {
        ConsumerCount += consumers;
        ExceptionCount += exceptions;
        ResumeAfterConsumerId = lastConsumerId;
        LastHeartbeatAt = now;
    }

    public void Complete(DateTime completedAt)
    {
        if (Status != BillingRunStatus.Running)
            throw new InvalidOperationException($"Cannot complete a billing run that is {Status} — only a Running run can be completed.");

        Status = ExceptionCount > 0 ? BillingRunStatus.CompletedWithExceptions : BillingRunStatus.Completed;
        CompletedAt = completedAt;
    }

    public void Fail(DateTime completedAt)
    {
        Status = BillingRunStatus.Failed;
        CompletedAt = completedAt;
    }
}
