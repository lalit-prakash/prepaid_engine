using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// An operational record of one batch billing run (currently only "DLP_DAILY" — the daily
/// settlement pass). Unique per <c>RunType + BillingDate</c>, preventing two daily runs from
/// being accidentally created for the same date.
/// </summary>
public class BillingRun
{
    public const string DlpDailyRunType = "DLP_DAILY";

    public Guid Id { get; private set; }
    public string RunType { get; private set; }
    public DateOnly BillingDate { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public BillingRunStatus Status { get; private set; }
    public int ConsumerCount { get; private set; }
    public int ExceptionCount { get; private set; }

    public BillingRun(Guid id, string runType, DateOnly billingDate, DateTime startedAt)
    {
        if (string.IsNullOrWhiteSpace(runType))
            throw new ArgumentException("A run type is required.", nameof(runType));

        Id = id;
        RunType = runType;
        BillingDate = billingDate;
        StartedAt = startedAt;
        Status = BillingRunStatus.Running;
    }

    // EF Core / serialization
    private BillingRun()
    {
        RunType = string.Empty;
    }

    public void RecordConsumer() => ConsumerCount++;

    public void RecordException() => ExceptionCount++;

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
