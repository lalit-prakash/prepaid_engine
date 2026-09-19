namespace PrepaidEngine.Domain.Entities;

public enum ReportJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    /// <summary>The file was removed after its retention period.</summary>
    Expired = 4,
}

/// <summary>
/// A request for a full CSV export of a report, produced in the background so it is not limited to the 5,000 rows a report
/// page shows. The request (which report and its filters) is stored as JSON; a worker writes the file in chunks and records
/// the row count and file name; the requester downloads it. Files are removed after a retention period.
/// </summary>
public class ReportJob
{
    public Guid Id { get; private set; }
    public string ReportKey { get; private set; } = string.Empty;
    public string ParametersJson { get; private set; } = "{}";
    public ReportJobStatus Status { get; private set; }
    public string RequestedBy { get; private set; } = string.Empty;
    public DateTime RequestedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public long RowCount { get; private set; }
    public long? FileSizeBytes { get; private set; }
    public string? FileName { get; private set; }
    public string? Error { get; private set; }

    public ReportJob(Guid id, string reportKey, string parametersJson, string requestedBy, DateTime requestedAt)
    {
        if (string.IsNullOrWhiteSpace(reportKey)) throw new ArgumentException("A report is required.", nameof(reportKey));
        if (string.IsNullOrWhiteSpace(requestedBy)) throw new ArgumentException("A requester is required.", nameof(requestedBy));

        Id = id;
        ReportKey = reportKey;
        ParametersJson = parametersJson;
        RequestedBy = requestedBy;
        RequestedAt = requestedAt;
        Status = ReportJobStatus.Queued;
    }

    // EF Core / serialization
    private ReportJob() { }

    /// <summary>Records that a worker has claimed this job (the claim itself is a conditional database update).</summary>
    public void MarkRunning(DateTime at)
    {
        StartedAt = at;
        Status = ReportJobStatus.Running;
    }

    public void RecordProgress(long rows) => RowCount = rows;

    public void Complete(DateTime at, string fileName, long rows, long sizeBytes, DateTime expiresAt)
    {
        if (Status != ReportJobStatus.Running) throw new InvalidOperationException($"Cannot complete a job that is {Status}.");
        Status = ReportJobStatus.Completed;
        CompletedAt = at;
        FileName = fileName;
        RowCount = rows;
        FileSizeBytes = sizeBytes;
        ExpiresAt = expiresAt;
    }

    public void Fail(DateTime at, string error)
    {
        Status = ReportJobStatus.Failed;
        CompletedAt = at;
        Error = error.Length > 500 ? error[..500] : error;
    }

    public void Expire()
    {
        if (Status != ReportJobStatus.Completed) throw new InvalidOperationException($"Only a completed job expires (this one is {Status}).");
        Status = ReportJobStatus.Expired;
        FileName = null;
    }
}
