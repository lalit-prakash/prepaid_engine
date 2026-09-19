using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrepaidEngine.Api.Reports.ReportJobs;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.Reports;

public class CsvTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    public void Cells_are_quoted_only_when_needed(string? input, string expected)
        => Assert.Equal(expected, Csv.Cell(input));

    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+91 98765", "'+91 98765")]
    [InlineData("-5", "'-5")]
    [InlineData("@cmd", "'@cmd")]
    public void Text_a_spreadsheet_would_run_as_a_formula_is_defused(string input, string expected)
        => Assert.Equal(expected, Csv.Cell(input));
}

public class ReportJobEntityTests
{
    private static ReportJob NewJob() => new(Guid.NewGuid(), "billing", "{}", "alice", DateTime.UtcNow);

    [Fact]
    public void A_new_job_is_queued_and_moves_through_running_to_completed()
    {
        var job = NewJob();
        Assert.Equal(ReportJobStatus.Queued, job.Status);

        job.MarkRunning(DateTime.UtcNow);
        job.Complete(DateTime.UtcNow, "f.csv", 42, 1000, DateTime.UtcNow.AddDays(7));

        Assert.Equal(ReportJobStatus.Completed, job.Status);
        Assert.Equal(42, job.RowCount);
        Assert.Equal("f.csv", job.FileName);
    }

    [Fact]
    public void Only_a_running_job_can_complete_and_only_a_completed_one_can_expire()
    {
        Assert.Throws<InvalidOperationException>(() => NewJob().Complete(DateTime.UtcNow, "f.csv", 1, 1, DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => NewJob().Expire());
    }

    [Fact]
    public void Failure_keeps_a_bounded_message()
    {
        var job = NewJob();
        job.Fail(DateTime.UtcNow, new string('x', 900));
        Assert.Equal(ReportJobStatus.Failed, job.Status);
        Assert.Equal(500, job.Error!.Length);
    }
}

/// <summary>The runner claims a queued job, writes the CSV in chunks, and cleans up, against a real (SQLite) database.</summary>
public class ReportJobRunnerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pe-report-tests-" + Guid.NewGuid().ToString("N"));

    public ReportJobRunnerTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ReportJobRunner Runner(int chunk = 2, long maxRows = 1000)
        => new(_db, Options.Create(new ReportJobOptions { OutputDirectory = _dir, ChunkSize = chunk, MaxRows = maxRows, RetentionDays = 7 }), NullLogger<ReportJobRunner>.Instance);

    private async Task SeedFailedRechargesAsync(int count)
    {
        var meter = new SmartMeter(Guid.NewGuid(), "M-EXP", MeterPhase.SinglePhase);
        var consumer = new Consumer(Guid.NewGuid(), "EXP-1", "Export, \"Quoted\" Name", "Street", meter, 1m);
        _db.Meters.Add(meter);
        _db.Consumers.Add(consumer);
        for (var i = 0; i < count; i++)
        {
            var r = new RechargeTransaction(Guid.NewGuid(), consumer.Id, 500m + i, "RMS-" + i, new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(i));
            r.MarkFailed(DateTime.UtcNow);
            _db.RechargeTransactions.Add(r);
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private async Task<Guid> QueueAsync(string report = ReportExports.RechargeFailures)
    {
        var job = new ReportJob(Guid.NewGuid(), report, "{}", "alice", DateTime.UtcNow);
        _db.ReportJobs.Add(job);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return job.Id;
    }

    [Fact]
    public async Task Writes_every_row_in_chunks_newest_first_with_a_header_and_quoted_cells()
    {
        await SeedFailedRechargesAsync(5);
        var id = await QueueAsync();

        Assert.True(await Runner(chunk: 2).RunNextAsync(CancellationToken.None));

        var job = await _db.ReportJobs.AsNoTracking().SingleAsync(j => j.Id == id);
        Assert.Equal(ReportJobStatus.Completed, job.Status);
        Assert.Equal(5, job.RowCount);
        var lines = (await File.ReadAllTextAsync(Path.Combine(_dir, job.FileName!))).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, lines.Length); // header + 5 rows
        Assert.StartsWith("Initiated,Account,Consumer,Zone", lines[0]);
        Assert.Contains("\"Export, \"\"Quoted\"\" Name\"", lines[1]);
        Assert.Contains("RMS-4", lines[1]); // newest first
        Assert.Contains("RMS-0", lines[5]);
        Assert.False(File.Exists(Path.Combine(_dir, id.ToString("N") + ".csv.part")));
    }

    [Fact]
    public async Task An_empty_report_still_produces_a_file_with_just_the_header()
    {
        var id = await QueueAsync();

        await Runner().RunNextAsync(CancellationToken.None);

        var job = await _db.ReportJobs.AsNoTracking().SingleAsync(j => j.Id == id);
        Assert.Equal(ReportJobStatus.Completed, job.Status);
        Assert.Equal(0, job.RowCount);
    }

    [Fact]
    public async Task Nothing_queued_means_nothing_to_do()
        => Assert.False(await Runner().RunNextAsync(CancellationToken.None));

    [Fact]
    public async Task A_job_over_the_row_limit_fails_cleanly_and_leaves_no_partial_file()
    {
        await SeedFailedRechargesAsync(5);
        var id = await QueueAsync();

        await Runner(chunk: 2, maxRows: 3).RunNextAsync(CancellationToken.None);

        var job = await _db.ReportJobs.AsNoTracking().SingleAsync(j => j.Id == id);
        Assert.Equal(ReportJobStatus.Failed, job.Status);
        Assert.Contains("limit", job.Error);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task A_job_can_only_be_run_once()
    {
        await SeedFailedRechargesAsync(1);
        await QueueAsync();
        var runner = Runner();

        Assert.True(await runner.RunNextAsync(CancellationToken.None));
        Assert.False(await runner.RunNextAsync(CancellationToken.None)); // already completed, nothing left queued
    }

    [Fact]
    public async Task Expired_files_are_removed_and_the_job_marked_expired()
    {
        await SeedFailedRechargesAsync(1);
        var id = await QueueAsync();
        var runner = Runner();
        await runner.RunNextAsync(CancellationToken.None);
        var file = Path.Combine(_dir, (await _db.ReportJobs.AsNoTracking().SingleAsync(j => j.Id == id)).FileName!);
        Assert.True(File.Exists(file));

        Assert.Equal(0, await runner.ExpireOldAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.Equal(1, await runner.ExpireOldAsync(DateTime.UtcNow.AddDays(8), CancellationToken.None));

        Assert.False(File.Exists(file));
        Assert.Equal(ReportJobStatus.Expired, (await _db.ReportJobs.AsNoTracking().SingleAsync(j => j.Id == id)).Status);
    }

    [Fact]
    public async Task A_job_stuck_running_is_failed_so_it_does_not_look_busy_forever()
    {
        var id = await QueueAsync();
        await _db.ReportJobs.Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ReportJobStatus.Running).SetProperty(j => j.StartedAt, DateTime.UtcNow.AddHours(-5)));

        Assert.Equal(1, await Runner().FailInterruptedAsync(DateTime.UtcNow, TimeSpan.FromHours(2), CancellationToken.None));

        Assert.Equal(ReportJobStatus.Failed, (await _db.ReportJobs.AsNoTracking().SingleAsync(j => j.Id == id)).Status);
    }
}
