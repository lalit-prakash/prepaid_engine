using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Endpoints;

/// <summary>
/// Keyset-paged search plus database-side summary counts for the meter credit, RC/DC and notification lists.
/// These lists grow with every recharge, command and message, so the pages no longer download them whole and count in
/// the browser. Search is newest first with an opaque cursor (<c>{created ticks}_{id}</c>), a page is at most 100 rows,
/// and each summary is a handful of counts computed in SQL.
/// </summary>
public static class PagedListEndpoints
{
    private const int MaxPage = 100;

    private static (string Prefix, string Contains) Terms(string q)
    {
        var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return (term + "%", "%" + term + "%");
    }

    private static bool TryCursor(string? after, out DateTime at, out Guid id)
    {
        at = default; id = Guid.Empty;
        var parts = after!.Split('_', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out id)) return false;
        at = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }

    private static string Cursor(DateTime at, Guid id) => $"{at.Ticks}_{id}";

    public static void MapPagedListEndpoints(this WebApplication app)
    {
        // ---------------------------------------------------------------- meter credit (meter commands)
        app.MapGet("/api/v1/meter-commands/search", async (
            string? q, string? status, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPage);
            var query =
                from m in db.MeterCommands.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on m.ConsumerId equals c.Id
                join r in db.RechargeTransactions.AsNoTracking() on m.RechargeTransactionId equals r.Id
                select new { m, c, r };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = Terms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.r.RmsReferenceId, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }
            query = status switch
            {
                "Acknowledged" => query.Where(x => x.m.Status == MeterCommandStatus.Acknowledged),
                "Failed" => query.Where(x => x.m.Status == MeterCommandStatus.Failed),
                "TimedOut" => query.Where(x => x.m.Status == MeterCommandStatus.TimedOut),
                "Pending" => query.Where(x => x.m.Status == MeterCommandStatus.Queued || x.m.Status == MeterCommandStatus.Sent),
                "Retried" => query.Where(x => x.m.RetryCount > 0),
                _ => query,
            };

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                if (!TryCursor(after, out var at, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
                query = query.Where(x => x.m.CreatedAt < at || (x.m.CreatedAt == at && x.m.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.m.CreatedAt).ThenByDescending(x => x.m.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.m.Id, x.c.AccountNumber, x.c.Name, x.m.CreditAmount, x.m.Status, x.m.RetryCount, x.m.ErrorMessage,
                    x.m.CreatedAt, x.m.SentAt, x.m.AcknowledgedAt, RechargeTransactionId = x.r.Id, x.r.RmsReferenceId,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? Cursor(items[^1].CreatedAt, items[^1].Id) : null, totalCount });
        })
        .WithName("SearchMeterCommands")
        .RequireAuthorization();

        app.MapGet("/api/v1/meter-commands/summary", async (PrepaidEngineDbContext db) =>
        {
            var byStatus = await db.MeterCommands.AsNoTracking().GroupBy(m => m.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
            var retried = await db.MeterCommands.AsNoTracking().CountAsync(m => m.RetryCount > 0);
            int N(MeterCommandStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            return Results.Ok(new
            {
                Total = byStatus.Sum(x => x.Count),
                Acknowledged = N(MeterCommandStatus.Acknowledged),
                Failed = N(MeterCommandStatus.Failed),
                TimedOut = N(MeterCommandStatus.TimedOut),
                Pending = N(MeterCommandStatus.Queued) + N(MeterCommandStatus.Sent),
                Retried = retried,
            });
        })
        .WithName("MeterCommandSummary")
        .RequireAuthorization();

        // ---------------------------------------------------------------- RC / DC (connectivity commands)
        app.MapGet("/api/v1/connectivity-commands/search", async (
            string? q, ConnectivityCommandType? type, string? status, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPage);
            var query =
                from cmd in db.ConnectivityCommands.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on cmd.ConsumerId equals c.Id
                select new { cmd, c };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = Terms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }
            if (type.HasValue) query = query.Where(x => x.cmd.CommandType == type.Value);
            query = status switch
            {
                "Acknowledged" => query.Where(x => x.cmd.Status == ConnectivityCommandStatus.Acknowledged),
                "FailedOrTimedOut" => query.Where(x => x.cmd.Status == ConnectivityCommandStatus.Failed || x.cmd.Status == ConnectivityCommandStatus.TimedOut),
                "Pending" => query.Where(x => x.cmd.Status == ConnectivityCommandStatus.Queued || x.cmd.Status == ConnectivityCommandStatus.Sent),
                _ => query,
            };

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                if (!TryCursor(after, out var at, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
                query = query.Where(x => x.cmd.CreatedAt < at || (x.cmd.CreatedAt == at && x.cmd.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.cmd.CreatedAt).ThenByDescending(x => x.cmd.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.cmd.Id, x.c.AccountNumber, x.c.Name, x.cmd.CommandType, x.cmd.Reason, x.cmd.Status, x.cmd.RetryCount,
                    x.cmd.ErrorMessage, x.cmd.CreatedAt, x.cmd.SentAt, x.cmd.AcknowledgedAt,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? Cursor(items[^1].CreatedAt, items[^1].Id) : null, totalCount });
        })
        .WithName("SearchConnectivityCommands")
        .RequireAuthorization();

        app.MapGet("/api/v1/connectivity-commands/summary", async (PrepaidEngineDbContext db) =>
        {
            var byStatus = await db.ConnectivityCommands.AsNoTracking().GroupBy(c => c.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
            var byType = await db.ConnectivityCommands.AsNoTracking().GroupBy(c => c.CommandType).Select(g => new { Type = g.Key, Count = g.Count() }).ToListAsync();
            int N(ConnectivityCommandStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            int T(ConnectivityCommandType t) => byType.FirstOrDefault(x => x.Type == t)?.Count ?? 0;
            return Results.Ok(new
            {
                Total = byStatus.Sum(x => x.Count),
                Disconnects = T(ConnectivityCommandType.Disconnect),
                Reconnects = T(ConnectivityCommandType.Reconnect),
                Acknowledged = N(ConnectivityCommandStatus.Acknowledged),
                FailedOrTimedOut = N(ConnectivityCommandStatus.Failed) + N(ConnectivityCommandStatus.TimedOut),
                Pending = N(ConnectivityCommandStatus.Queued) + N(ConnectivityCommandStatus.Sent),
            });
        })
        .WithName("ConnectivityCommandSummary")
        .RequireAuthorization();

        // ---------------------------------------------------------------- notifications
        app.MapGet("/api/v1/notifications/search", async (
            string? q, NotificationEventType? eventType, NotificationStatus? status, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPage);
            var query =
                from n in db.NotificationEvents.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on n.ConsumerId equals c.Id
                select new { n, c };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = Terms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\") || EF.Functions.ILike(x.n.Message, contains, "\\"));
            }
            if (eventType.HasValue) query = query.Where(x => x.n.EventType == eventType.Value);
            if (status.HasValue) query = query.Where(x => x.n.Status == status.Value);

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                if (!TryCursor(after, out var at, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
                query = query.Where(x => x.n.CreatedAt < at || (x.n.CreatedAt == at && x.n.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.n.CreatedAt).ThenByDescending(x => x.n.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.n.Id, x.c.AccountNumber, x.c.Name, x.n.EventType, x.n.Message, x.n.Status, x.n.CreatedAt, x.n.SentAt, x.n.ProviderReference,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? Cursor(items[^1].CreatedAt, items[^1].Id) : null, totalCount });
        })
        .WithName("SearchNotifications")
        .RequireAuthorization();

        app.MapGet("/api/v1/notifications/summary", async (PrepaidEngineDbContext db) =>
        {
            var byStatus = await db.NotificationEvents.AsNoTracking().GroupBy(n => n.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
            int N(NotificationStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            return Results.Ok(new
            {
                Total = byStatus.Sum(x => x.Count),
                Pending = N(NotificationStatus.Pending),
                Sent = N(NotificationStatus.Sent),
                Failed = N(NotificationStatus.Failed),
            });
        })
        .WithName("NotificationSummary")
        .RequireAuthorization();
    }
}
