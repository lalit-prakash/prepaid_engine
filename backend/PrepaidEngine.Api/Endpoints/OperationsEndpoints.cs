using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Api.Security;
using PrepaidEngine.Api.Dashboard;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Application.Conversion;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Application.MeterData;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Conversion;
using PrepaidEngine.Infrastructure.MeterCommands;
using PrepaidEngine.Infrastructure.MeterData;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Sla;
using PrepaidEngine.Infrastructure.Tariffs;
using PrepaidEngine.Application.Sla;
using PrepaidEngine.Infrastructure.Persistence.Seed;
using PrepaidEngine.Infrastructure.Rms;
using static PrepaidEngine.Api.ApiHelpers;

namespace PrepaidEngine.Api.Endpoints;

/// <summary>Operations endpoints, moved out of Program.cs unchanged.</summary>
public static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this WebApplication app)
    {
        // --- Operational exceptions: real, generated automatically (see RaiseException above) --------
        // whenever a MeterCommand or ConnectivityCommand reaches Failed/TimedOut — never hand-entered.
        app.MapGet("/api/v1/exceptions", async (HttpContext http, PrepaidEngineDbContext db) =>
        {
            var exceptions = await (
                from e in db.OperationalExceptions
                join c in db.Consumers on e.ConsumerId equals c.Id
                orderby e.CreatedAt descending
                select new
                {
                    e.Id,
                    c.AccountNumber,
                    c.Name,
                    e.SourceType,
                    e.SourceId,
                    e.Description,
                    e.Status,
                    e.ResolutionNote,
                    e.CreatedAt,
                    e.ResolvedAt,
                })
                .ToCappedListAsync(http);

            return Results.Ok(exceptions);
        })
        .WithName("ListOperationalExceptions")
        .RequireAuthorization();


        app.MapGet("/api/v1/exceptions/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
        {
            var exception = await db.OperationalExceptions.FirstOrDefaultAsync(e => e.Id == id);
            if (exception is null)
                return Results.NotFound();

            var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == exception.ConsumerId);
            if (consumer is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                    title: "Operational exception references missing data",
                    detail: $"Operational exception {id} references a consumer that no longer exists.");
            }

            return Results.Ok(new
            {
                exception.Id,
                Consumer = new { consumer.AccountNumber, consumer.Name },
                exception.SourceType,
                exception.SourceId,
                exception.Description,
                exception.Status,
                exception.ResolutionNote,
                exception.CreatedAt,
                exception.ResolvedAt,
            });
        })
        .WithName("GetOperationalExceptionById")
        .RequireAuthorization();


        app.MapPost("/api/v1/exceptions/{id:guid}/resolve", async (Guid id, ResolutionRequest request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            var exception = await db.OperationalExceptions.FirstOrDefaultAsync(e => e.Id == id);
            if (exception is null)
                return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.Note))
                return Results.BadRequest(new { error = "A resolution note is required." });

            try
            {
                exception.Resolve(request.Note, DateTime.UtcNow);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            Audit(db, nameof(OperationalException), exception.Id.ToString(), "Resolved", user.Identity?.Name ?? "unknown", details: request.Note);
            await db.SaveChangesAsync();

            return Results.Ok(new { exception.Id, exception.Status, exception.ResolutionNote, exception.ResolvedAt });
        })
        .WithName("ResolveOperationalException")
        .RequireAuthorization("Operations");


        // --- SLA monitoring (Phase 3): real performance against configurable targets, computed from ---
        // timestamps this engine already records — see ISlaMonitoringService's doc comment.
        app.MapGet("/api/v1/sla", async (ISlaMonitoringService slaMonitoring) =>
        {
            var metrics = await slaMonitoring.GetSlaSummaryAsync();
            return Results.Ok(metrics);
        })
        .WithName("GetSlaSummary")
        .RequireAuthorization();


        // --- Revenue & Risk Indicators (Phase 3): real open-condition counts only — deliberately never
        // a fabricated monetary "revenue protected" figure (see RiskIndicatorsSummary's doc comment).
        app.MapGet("/api/v1/risk-indicators", async (PrepaidEngineDbContext db) =>
        {
            var summary = new RiskIndicatorsSummary(
                OpenExceptions: await db.OperationalExceptions.CountAsync(e => e.Status == OperationalExceptionStatus.Open),
                ActiveBillingHolds: await db.MeterBillingControls.CountAsync(m => m.ActualBillingBlocked),
                UnresolvedMeterAlarms: await db.MeterAlarms.CountAsync(a => a.Status != MeterAlarmStatus.Resolved),
                DisconnectedConsumers: await db.Consumers.CountAsync(c => c.ConnectionStatus == ConnectionStatus.Disconnected),
                FailedEnergyValidations: await db.EnergyValidationResults.CountAsync(v => v.Status == EnergyValidationStatus.Fail));

            return Results.Ok(summary);
        })
        .WithName("GetRiskIndicators")
        .RequireAuthorization();


        // Cross-consumer operator view — GetConsumerNotifications above is scoped to one consumer (e.g.
        // for a future Consumer 360 section); this backs a standalone Notification History page.
        app.MapGet("/api/v1/notifications", async (HttpContext http, PrepaidEngineDbContext db) =>
        {
            var notifications = await (
                from n in db.NotificationEvents
                join consumer in db.Consumers on n.ConsumerId equals consumer.Id
                orderby n.CreatedAt descending
                select new
                {
                    n.Id,
                    consumer.AccountNumber,
                    consumer.Name,
                    n.EventType,
                    n.Message,
                    n.Status,
                    n.CreatedAt,
                    n.SentAt,
                    n.ProviderReference,
                })
                .ToCappedListAsync(http);

            return Results.Ok(notifications);
        })
        .WithName("ListNotifications")
        .RequireAuthorization();
    }
}
