using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Reports.ReportJobs;

/// <summary>The body of a request for a full export: which report and the filters to apply.</summary>
public sealed record ReportJobRequest(
    string? Report, DateTime? From, DateTime? To, string? Status,
    Guid? ZoneId, Guid? CircleId, Guid? DivisionId, Guid? SubDivisionId, Guid? SubstationId, Guid? FeederId, Guid? DtrId);

/// <summary>
/// Full report exports run as background jobs so they are not limited to the rows a report page shows: request one
/// (POST), watch it (GET), download the CSV when it is done. Requesting and downloading need the Operations policy, because
/// an export can hold every consumer's details at once; each is audited. People see their own exports; Admin and IT see all.
/// </summary>
public static class ReportJobEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static bool SeesAll(ClaimsPrincipal user) => user.IsInRole("Admin") || user.IsInRole("IT");

    private static object Dto(ReportJob j) => new
    {
        j.Id, Report = j.ReportKey, Status = j.Status.ToString(), j.RequestedBy, j.RequestedAt, j.StartedAt, j.CompletedAt, j.ExpiresAt,
        j.RowCount, j.FileSizeBytes, j.Error,
        Parameters = JsonSerializer.Deserialize<JsonElement>(j.ParametersJson),
        CanDownload = j.Status == ReportJobStatus.Completed,
    };

    public static void MapReportJobEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/report-jobs", async (ReportJobRequest request, PrepaidEngineDbContext db, ClaimsPrincipal user, IOptions<ReportJobOptions> options) =>
        {
            if (request.Report is null || !ReportExports.Keys.Contains(request.Report))
                return Results.BadRequest(new { error = $"Unknown report. Exportable reports: {string.Join(", ", ReportExports.Keys)}." });
            if (request.From.HasValue && request.To.HasValue && request.From.Value.Date > request.To.Value.Date)
                return Results.BadRequest(new { error = "The start date must not be after the end date." });

            var who = user.Identity?.Name ?? "unknown";
            var active = await db.ReportJobs.CountAsync(j => j.RequestedBy == who && (j.Status == ReportJobStatus.Queued || j.Status == ReportJobStatus.Running));
            if (active >= options.Value.MaxActivePerUser)
                return Results.Json(new { error = $"You already have {active} exports waiting or running. Wait for one to finish." }, statusCode: StatusCodes.Status409Conflict);

            var parameters = new ReportJobParameters(request.From, request.To, request.Status, request.ZoneId, request.CircleId, request.DivisionId,
                request.SubDivisionId, request.SubstationId, request.FeederId, request.DtrId);
            var job = new ReportJob(Guid.NewGuid(), request.Report, JsonSerializer.Serialize(parameters, Json), who, DateTime.UtcNow);
            db.ReportJobs.Add(job);
            db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), nameof(ReportJob), job.Id.ToString(), "REPORT_EXPORT_REQUESTED", who, DateTime.UtcNow, details: $"{request.Report} {job.ParametersJson}"));
            await db.SaveChangesAsync();
            return Results.Accepted($"/api/v1/report-jobs/{job.Id}", Dto(job));
        })
        .WithName("RequestReportExport")
        .RequireAuthorization("Operations");

        app.MapGet("/api/v1/report-jobs", async (PrepaidEngineDbContext db, ClaimsPrincipal user) =>
        {
            var who = user.Identity?.Name ?? "unknown";
            var jobs = await db.ReportJobs.AsNoTracking()
                .Where(j => SeesAll(user) || j.RequestedBy == who)
                .OrderByDescending(j => j.RequestedAt).ThenByDescending(j => j.Id)
                .Take(50).ToListAsync();
            return Results.Ok(jobs.Select(Dto));
        })
        .WithName("ListReportExports")
        .RequireAuthorization();

        app.MapGet("/api/v1/report-jobs/{id:guid}", async (Guid id, PrepaidEngineDbContext db, ClaimsPrincipal user) =>
        {
            var job = await db.ReportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
            if (job is null || !(SeesAll(user) || job.RequestedBy == user.Identity?.Name)) return Results.NotFound();
            return Results.Ok(Dto(job));
        })
        .WithName("GetReportExport")
        .RequireAuthorization();

        app.MapGet("/api/v1/report-jobs/{id:guid}/download", async (Guid id, PrepaidEngineDbContext db, ClaimsPrincipal user, ReportJobRunner runner) =>
        {
            var job = await db.ReportJobs.FirstOrDefaultAsync(j => j.Id == id);
            var who = user.Identity?.Name ?? "unknown";
            if (job is null || !(SeesAll(user) || job.RequestedBy == who)) return Results.NotFound();
            if (job.Status != ReportJobStatus.Completed || job.FileName is null) return Results.Conflict(new { error = $"This export is {job.Status.ToString().ToLowerInvariant()}, so there is nothing to download." });

            var path = runner.PathFor(job.FileName);
            if (!File.Exists(path)) return Results.NotFound(new { error = "The file is no longer on the server." });

            db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), nameof(ReportJob), job.Id.ToString(), "REPORT_EXPORT_DOWNLOADED", who, DateTime.UtcNow, details: job.ReportKey));
            await db.SaveChangesAsync();
            var name = $"{job.ReportKey}-{job.RequestedAt:yyyyMMdd-HHmm}.csv";
            return Results.File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), "text/csv", name);
        })
        .WithName("DownloadReportExport")
        .RequireAuthorization("Operations");
    }
}
