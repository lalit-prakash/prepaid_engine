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

/// <summary>Audit endpoints, moved out of Program.cs unchanged.</summary>
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        // --- Audit entries: an immutable, append-only log — read-only, filterable by entity type and ---
        // a date range (see README.md for exactly which actions append an entry).
        // Server-side searchable, keyset-paginated audit log. Read-only by design: there is no update or
        // delete endpoint for audit entries anywhere in this API. Newest first; the cursor is
        // "<OccurredAt ticks>_<Id>". q matches entity id / action / actor by prefix and details by substring.
        app.MapGet("/api/v1/audit-entries/search", async (
            string? q, string? entityType, string? actor, DateTime? from, DateTime? to, string? after, int? pageSize,
            PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            var query = db.AuditEntries.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(entityType))
                query = query.Where(a => a.EntityType == entityType);
            if (!string.IsNullOrWhiteSpace(actor))
                query = query.Where(a => a.Actor == actor);
            if (from.HasValue)
                query = query.Where(a => a.OccurredAt >= DateTime.SpecifyKind(from.Value, DateTimeKind.Utc));
            if (to.HasValue)
                query = query.Where(a => a.OccurredAt < DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc));
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                var prefix = term + "%";
                var contains = "%" + term + "%";
                query = query.Where(a =>
                    EF.Functions.ILike(a.EntityId, prefix, "\\") ||
                    EF.Functions.ILike(a.Action, prefix, "\\") ||
                    EF.Functions.ILike(a.Actor, prefix, "\\") ||
                    (a.CorrelationId != null && EF.Functions.ILike(a.CorrelationId, prefix, "\\")) ||
                    (a.Details != null && EF.Functions.ILike(a.Details, contains, "\\")));
            }

            var totalCount = await query.CountAsync();

            if (!string.IsNullOrEmpty(after))
            {
                var parts = after.Split('_', 2);
                if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var afterId))
                    return Results.BadRequest(new { error = "Invalid cursor." });
                var afterAt = new DateTime(ticks, DateTimeKind.Utc);
                query = query.Where(a => a.OccurredAt < afterAt || (a.OccurredAt == afterAt && a.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
                .Take(size + 1)
                .Select(a => new { a.Id, a.EntityType, a.EntityId, a.Action, a.Actor, a.ActorRole, a.SourceIp, a.CorrelationId, a.OldValue, a.NewValue, a.Details, a.OccurredAt })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            var nextCursor = hasMore ? $"{items[^1].OccurredAt.Ticks}_{items[^1].Id}" : null;
            return Results.Ok(new { items, nextCursor, totalCount });
        })
        .WithName("SearchAuditEntries")
        .RequireAuthorization();


        // Totals plus the distinct entity types and actors that populate the filter dropdowns.
        app.MapGet("/api/v1/audit-entries/summary", async (PrepaidEngineDbContext db) =>
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var total = await db.AuditEntries.CountAsync();
            var last24h = await db.AuditEntries.CountAsync(a => a.OccurredAt >= since);
            var entityTypes = await db.AuditEntries.Select(a => a.EntityType).Distinct().OrderBy(x => x).ToListAsync();
            var actors = await db.AuditEntries.Select(a => a.Actor).Distinct().OrderBy(x => x).Take(200).ToListAsync();
            return Results.Ok(new { Total = total, Last24Hours = last24h, EntityTypes = entityTypes, Actors = actors });
        })
        .WithName("GetAuditSummary")
        .RequireAuthorization();


        app.MapGet("/api/v1/audit-entries", async (HttpContext http, PrepaidEngineDbContext db, string? entityType, string? entityId, DateTime? from, DateTime? to) =>
        {
            var query = db.AuditEntries.AsQueryable();

            if (!string.IsNullOrWhiteSpace(entityId))
                query = query.Where(a => a.EntityId == entityId);
            if (!string.IsNullOrWhiteSpace(entityType))
                query = query.Where(a => a.EntityType == entityType);
            if (from.HasValue)
                query = query.Where(a => a.OccurredAt >= from.Value);
            if (to.HasValue)
                query = query.Where(a => a.OccurredAt <= to.Value);

            var entries = await query
                .OrderByDescending(a => a.OccurredAt)
                .Select(a => new
                {
                    a.Id,
                    a.EntityType,
                    a.EntityId,
                    a.Action,
                    a.Actor,
                    a.OldValue,
                    a.NewValue,
                    a.Details,
                    a.OccurredAt,
                })
                .Take(string.IsNullOrWhiteSpace(entityId) ? int.MaxValue : 100)
                .ToCappedListAsync(http);

            return Results.Ok(entries);
        })
        .WithName("ListAuditEntries")
        .RequireAuthorization();
    }
}
