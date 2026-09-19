using System.Collections.Concurrent;

namespace PrepaidEngine.Api.Health;

/// <summary>What one background worker last reported.</summary>
public sealed record WorkerStatus(
    string Name, int ExpectedEverySeconds, DateTime? LastSuccessAt, DateTime? LastFailureAt, string? LastError, long Runs, long Failures);

/// <summary>
/// Where each background worker records that it is alive: every round it reports a success or a failure. The health
/// page reads this to tell a worker that is running from one that has stopped or keeps failing. State is in memory
/// and per API instance, so it starts empty after a restart and shows only this instance's workers.
/// </summary>
public sealed class WorkerStatusRegistry
{
    private sealed class Entry
    {
        public int ExpectedEverySeconds;
        public DateTime? LastSuccessAt, LastFailureAt;
        public string? LastError;
        public long Runs, Failures;
    }

    private readonly ConcurrentDictionary<string, Entry> _workers = new();

    /// <summary>Registers a worker as expected, so it shows as "not yet run" instead of being missing.</summary>
    public void Expect(string name, int everySeconds)
        => _workers.GetOrAdd(name, _ => new Entry { ExpectedEverySeconds = everySeconds });

    public void Success(string name, int everySeconds)
    {
        var e = _workers.GetOrAdd(name, _ => new Entry { ExpectedEverySeconds = everySeconds });
        lock (e) { e.ExpectedEverySeconds = everySeconds; e.Runs++; e.LastSuccessAt = DateTime.UtcNow; }
    }

    public void Failure(string name, int everySeconds, Exception ex)
    {
        var e = _workers.GetOrAdd(name, _ => new Entry { ExpectedEverySeconds = everySeconds });
        lock (e) { e.ExpectedEverySeconds = everySeconds; e.Runs++; e.Failures++; e.LastFailureAt = DateTime.UtcNow; e.LastError = ex.GetType().Name + ": " + Truncate(ex.Message); }
    }

    public IReadOnlyList<WorkerStatus> Snapshot()
        => _workers.OrderBy(w => w.Key).Select(w =>
        {
            lock (w.Value) return new WorkerStatus(w.Key, w.Value.ExpectedEverySeconds, w.Value.LastSuccessAt, w.Value.LastFailureAt, w.Value.LastError, w.Value.Runs, w.Value.Failures);
        }).ToList();

    /// <summary>Healthy while a success was reported within three expected intervals (plus a 30 second grace).</summary>
    public static string StateOf(WorkerStatus w, DateTime utcNow)
    {
        if (w.LastSuccessAt is null) return w.LastFailureAt is null ? "NotYetRun" : "Failing";
        var limit = TimeSpan.FromSeconds(w.ExpectedEverySeconds * 3 + 30);
        if (utcNow - w.LastSuccessAt > limit) return "Stale";
        return w.LastFailureAt is { } f && f > w.LastSuccessAt ? "Failing" : "Healthy";
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200];
}
