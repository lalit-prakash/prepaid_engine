using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Reports.ReportJobs;

/// <summary>Bound from the "ReportJobs" configuration section.</summary>
public sealed class ReportJobOptions
{
    public const string SectionName = "ReportJobs";

    /// <summary>Where finished exports are written. A relative path is taken from the API's working directory.</summary>
    public string OutputDirectory { get; set; } = "report-exports";

    /// <summary>How long a finished file is kept before it is removed.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Rows fetched and written per chunk, so memory stays flat however large the export is.</summary>
    public int ChunkSize { get; set; } = 5000;

    /// <summary>A job stops with an error rather than write more than this many rows.</summary>
    public long MaxRows { get; set; } = 5_000_000;

    /// <summary>Exports one person may have waiting or running at once.</summary>
    public int MaxActivePerUser { get; set; } = 3;

    public int PollSeconds { get; set; } = 5;
}

/// <summary>
/// Turns queued report requests into CSV files. A job is claimed with a conditional update (Queued to Running) so several
/// instances never build the same file, written in chunks to a temporary file and renamed when complete so a download never
/// sees a half-written export, and cleaned up (file removed, job marked Expired) after the retention period.
/// </summary>
public class ReportJobRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PrepaidEngineDbContext _db;
    private readonly ReportJobOptions _options;
    private readonly ILogger<ReportJobRunner> _logger;

    public ReportJobRunner(PrepaidEngineDbContext db, IOptions<ReportJobOptions> options, ILogger<ReportJobRunner> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public string PathFor(string fileName) => Path.Combine(Path.GetFullPath(_options.OutputDirectory), fileName);

    /// <summary>Runs the oldest queued job, if any. Returns whether a job was picked up.</summary>
    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        var id = await _db.ReportJobs.AsNoTracking()
            .Where(j => j.Status == ReportJobStatus.Queued)
            .OrderBy(j => j.RequestedAt)
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(ct);
        if (id is null) return false;

        var now = DateTime.UtcNow;
        var claimed = await _db.ReportJobs
            .Where(j => j.Id == id && j.Status == ReportJobStatus.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ReportJobStatus.Running).SetProperty(j => j.StartedAt, now), ct);
        if (claimed != 1) return true; // another instance took it; look again straight away

        var job = await _db.ReportJobs.FirstAsync(j => j.Id == id, ct);
        var partial = PathFor(job.Id.ToString("N") + ".csv.part");
        try
        {
            Directory.CreateDirectory(Path.GetFullPath(_options.OutputDirectory));
            var parameters = JsonSerializer.Deserialize<ReportJobParameters>(job.ParametersJson, Json)
                ?? throw new InvalidOperationException("The job's parameters could not be read.");

            long rows = 0;
            await using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var writer = Csv.Open(stream))
            {
                await Csv.WriteRowAsync(writer, ReportExports.Headers(job.ReportKey));
                (DateTime At, Guid Id)? after = null;
                while (true)
                {
                    var chunk = await ReportExports.NextChunkAsync(_db, job.ReportKey, parameters, after, _options.ChunkSize, ct);
                    if (chunk.Count == 0) break;
                    if (rows + chunk.Count > _options.MaxRows)
                        throw new InvalidOperationException($"The export is larger than the limit of {_options.MaxRows:N0} rows; narrow the date range or network filter.");
                    foreach (var row in chunk) await Csv.WriteRowAsync(writer, row.Cells);
                    rows += chunk.Count;
                    after = (chunk[^1].At, chunk[^1].Id);
                    await _db.ReportJobs.Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.RowCount, rows), ct);
                    _db.ChangeTracker.Clear();
                    if (chunk.Count < _options.ChunkSize) break;
                }
            }

            var fileName = job.Id.ToString("N") + ".csv";
            File.Move(partial, PathFor(fileName), overwrite: true);
            var done = DateTime.UtcNow;
            job = await _db.ReportJobs.FirstAsync(j => j.Id == id, ct);
            job.Complete(done, fileName, rows, new FileInfo(PathFor(fileName)).Length, done.AddDays(_options.RetentionDays));
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Report job {JobId} failed.", id);
            TryDelete(partial);
            _db.ChangeTracker.Clear();
            var failed = await _db.ReportJobs.FirstAsync(j => j.Id == id, CancellationToken.None);
            failed.Fail(DateTime.UtcNow, ex.Message);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
        return true;
    }

    /// <summary>Removes files past their retention date and marks those jobs Expired.</summary>
    public async Task<int> ExpireOldAsync(DateTime nowUtc, CancellationToken ct)
    {
        var due = await _db.ReportJobs
            .Where(j => j.Status == ReportJobStatus.Completed && j.ExpiresAt != null && j.ExpiresAt < nowUtc)
            .Take(200).ToListAsync(ct);
        foreach (var job in due)
        {
            if (job.FileName is not null) TryDelete(PathFor(job.FileName));
            job.Expire();
        }
        if (due.Count > 0) await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return due.Count;
    }

    /// <summary>A job left Running long after it started lost its worker (for example the process was stopped); fail it so it does not look busy forever.</summary>
    public async Task<int> FailInterruptedAsync(DateTime nowUtc, TimeSpan olderThan, CancellationToken ct)
    {
        var cutoff = nowUtc - olderThan;
        var stuck = await _db.ReportJobs.Where(j => j.Status == ReportJobStatus.Running && j.StartedAt != null && j.StartedAt < cutoff).Take(50).ToListAsync(ct);
        foreach (var job in stuck)
        {
            TryDelete(PathFor(job.Id.ToString("N") + ".csv.part"));
            job.Fail(nowUtc, "The export was interrupted before it finished. Request it again.");
        }
        if (stuck.Count > 0) await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        return stuck.Count;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
