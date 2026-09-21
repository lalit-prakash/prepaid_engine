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
                    MeterNumber = x.c.Meter.MeterNumber,
                    Zone = x.c.Dtr != null ? x.c.Dtr.Feeder.Substation.SubDivision.Division.Circle.Zone.Name : null,
                    Circle = x.c.Dtr != null ? x.c.Dtr.Feeder.Substation.SubDivision.Division.Circle.Name : null,
                    Division = x.c.Dtr != null ? x.c.Dtr.Feeder.Substation.SubDivision.Division.Name : null,
                    SubDivision = x.c.Dtr != null ? x.c.Dtr.Feeder.Substation.SubDivision.Name : null,
                    Substation = x.c.Dtr != null ? x.c.Dtr.Feeder.Substation.Name : null,
                    Feeder = x.c.Dtr != null ? x.c.Dtr.Feeder.Name : null,
                    FeederCode = x.c.Dtr != null ? x.c.Dtr.Feeder.Code : null,
                    Dtr = x.c.Dtr != null ? x.c.Dtr.Name : null,
                    DtrCode = x.c.Dtr != null ? x.c.Dtr.Code : null,
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

        // ---------------------------------------------------------------- operational exceptions
        app.MapGet("/api/v1/exceptions/search", async (
            string? q, OperationalExceptionStatus? status, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPage);
            var query =
                from e in db.OperationalExceptions.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on e.ConsumerId equals c.Id
                select new { e, c };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = Terms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\") || EF.Functions.ILike(x.e.Description, contains, "\\"));
            }
            if (status.HasValue) query = query.Where(x => x.e.Status == status.Value);

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                if (!TryCursor(after, out var at, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
                query = query.Where(x => x.e.CreatedAt < at || (x.e.CreatedAt == at && x.e.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.e.CreatedAt).ThenByDescending(x => x.e.Id)
                .Take(size + 1)
                .Select(x => new { x.e.Id, x.c.AccountNumber, x.c.Name, x.e.SourceType, x.e.SourceId, x.e.Description, x.e.Status, x.e.ResolutionNote, x.e.CreatedAt, x.e.ResolvedAt })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? Cursor(items[^1].CreatedAt, items[^1].Id) : null, totalCount });
        })
        .WithName("SearchOperationalExceptions")
        .RequireAuthorization();

        app.MapGet("/api/v1/exceptions/summary", async (PrepaidEngineDbContext db) =>
        {
            var byStatus = await db.OperationalExceptions.AsNoTracking().GroupBy(e => e.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
            int N(OperationalExceptionStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            return Results.Ok(new { Total = byStatus.Sum(x => x.Count), Open = N(OperationalExceptionStatus.Open), Resolved = N(OperationalExceptionStatus.Resolved) });
        })
        .WithName("OperationalExceptionSummary")
        .RequireAuthorization();

        // ---------------------------------------------------------------- conversions
        app.MapGet("/api/v1/conversions/search", async (
            string? q, string? status, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPage);
            var query =
                from cv in db.ConversionRequests.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on cv.ConsumerId equals c.Id
                select new { cv, c };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = Terms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.cv.TransactionId, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }
            query = status switch
            {
                "Completed" => query.Where(x => x.cv.Status == ConversionStatus.Completed),
                "Rejected" => query.Where(x => x.cv.Status == ConversionStatus.Rejected),
                "Pending" => query.Where(x => x.cv.Status == ConversionStatus.Requested || x.cv.Status == ConversionStatus.Approved),
                _ => query,
            };

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                if (!TryCursor(after, out var at, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
                query = query.Where(x => x.cv.RequestedAt < at || (x.cv.RequestedAt == at && x.cv.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.cv.RequestedAt).ThenByDescending(x => x.cv.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.cv.Id, x.c.AccountNumber, x.c.Name, x.cv.TransactionId, x.cv.MeterSerialNumber, x.cv.RequestType, x.cv.ConsumerType,
                    x.cv.InitialReading, x.cv.InitialReadingDateTime, x.cv.ConversionDate, x.cv.GracePeriodEndDate, x.cv.Status, x.cv.DecisionNote,
                    x.cv.RequestedAt, x.cv.DecidedAt, x.cv.CompletedAt, x.cv.LastReadingDate, x.cv.LastBillingDate, x.cv.TemporaryDisconnectionDate,
                    x.cv.ReconnectionDate, x.cv.LastBillFrKwh, x.cv.LastBillFrKvah, x.cv.LastBillMaxDemandKw, x.cv.OutstandingAmount, x.cv.MeterStatus,
                    x.cv.IsPermanentConsumer, x.cv.FoaAmount, x.cv.DiaAmount, x.cv.ReadingAtConversion,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? Cursor(items[^1].RequestedAt, items[^1].Id) : null, totalCount });
        })
        .WithName("SearchConversions")
        .RequireAuthorization();

        app.MapGet("/api/v1/conversions/summary", async (PrepaidEngineDbContext db) =>
        {
            var byStatus = await db.ConversionRequests.AsNoTracking().GroupBy(c => c.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
            int N(ConversionStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            var credited = await db.ConversionRequests.AsNoTracking().Where(c => c.Status == ConversionStatus.Completed).SumAsync(c => (decimal?)(c.FoaAmount + c.DiaAmount)) ?? 0m;
            return Results.Ok(new
            {
                Total = byStatus.Sum(x => x.Count),
                Completed = N(ConversionStatus.Completed),
                Rejected = N(ConversionStatus.Rejected),
                Pending = N(ConversionStatus.Requested) + N(ConversionStatus.Approved),
                FoaDiaCredited = credited,
            });
        })
        .WithName("ConversionSummary")
        .RequireAuthorization();

        // ---------------------------------------------------------------- reconciliation adjustments
        app.MapGet("/api/v1/reconciliation-adjustments/search", async (
            string? q, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPage);
            var query =
                from r in db.ReconciliationAdjustments.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on r.ConsumerId equals c.Id
                select new { r, c };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = Terms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.r.Reference, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                if (!TryCursor(after, out var at, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
                query = query.Where(x => x.r.AppliedAt < at || (x.r.AppliedAt == at && x.r.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.r.AppliedAt).ThenByDescending(x => x.r.Id)
                .Take(size + 1)
                .Select(x => new { x.r.Id, x.c.AccountNumber, x.c.Name, x.r.Amount, x.r.PaymentMode, x.r.ReconciliationDate, x.r.Reference, x.r.BalanceAfter, x.r.AppliedAt })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? Cursor(items[^1].AppliedAt, items[^1].Id) : null, totalCount });
        })
        .WithName("SearchReconciliationAdjustments")
        .RequireAuthorization();

        app.MapGet("/api/v1/reconciliation-adjustments/summary", async (PrepaidEngineDbContext db) => Results.Ok(new
        {
            Total = await db.ReconciliationAdjustments.AsNoTracking().CountAsync(),
            TotalCredited = await db.ReconciliationAdjustments.AsNoTracking().Where(r => r.Amount > 0).SumAsync(r => (decimal?)r.Amount) ?? 0m,
            TotalDebited = -(await db.ReconciliationAdjustments.AsNoTracking().Where(r => r.Amount < 0).SumAsync(r => (decimal?)r.Amount) ?? 0m),
        }))
        .WithName("ReconciliationAdjustmentSummary")
        .RequireAuthorization();

        // ---------------------------------------------------------------- meter replacements
        app.MapGet("/api/v1/meter-replacements/search", async (
            string? q, MeterAssignmentEventType? eventType, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPage);
            var query =
                from a in db.MeterAssignments.AsNoTracking()
                join consumer in db.Consumers.AsNoTracking() on a.ConsumerId equals consumer.Id
                join newMeter in db.Meters.AsNoTracking() on a.NewMeterId equals newMeter.Id
                join oldMeter in db.Meters.AsNoTracking() on a.OldMeterId equals oldMeter.Id into oldMeterJoin
                from oldMeter in oldMeterJoin.DefaultIfEmpty()
                select new { a, consumer, newMeter, oldMeter };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = Terms(q);
                query = query.Where(x => EF.Functions.ILike(x.consumer.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.newMeter.MeterNumber, prefix, "\\")
                    || (x.oldMeter != null && EF.Functions.ILike(x.oldMeter.MeterNumber, prefix, "\\")) || EF.Functions.ILike(x.consumer.Name, contains, "\\"));
            }
            if (eventType.HasValue) query = query.Where(x => x.a.EventType == eventType.Value);

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                if (!TryCursor(after, out var at, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
                query = query.Where(x => x.a.RecordedAt < at || (x.a.RecordedAt == at && x.a.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.a.RecordedAt).ThenByDescending(x => x.a.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.a.Id, x.consumer.AccountNumber, x.consumer.Name, x.a.EventType,
                    OldMeterNumber = x.oldMeter != null ? x.oldMeter.MeterNumber : null,
                    NewMeterNumber = x.newMeter.MeterNumber,
                    x.a.EffectiveFrom, x.a.OldMeterClosingReadingKwh, x.a.NewMeterOpeningReadingKwh, x.a.Reason, x.a.RecordedAt,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? Cursor(items[^1].RecordedAt, items[^1].Id) : null, totalCount });
        })
        .WithName("SearchMeterReplacements")
        .RequireAuthorization();

        app.MapGet("/api/v1/meter-replacements/summary", async (PrepaidEngineDbContext db) =>
        {
            var byType = await db.MeterAssignments.AsNoTracking().GroupBy(a => a.EventType).Select(g => new { Type = g.Key, Count = g.Count() }).ToListAsync();
            int N(MeterAssignmentEventType t) => byType.FirstOrDefault(x => x.Type == t)?.Count ?? 0;
            return Results.Ok(new { Total = byType.Sum(x => x.Count), Replaced = N(MeterAssignmentEventType.Replaced), Installed = N(MeterAssignmentEventType.Installed) });
        })
        .WithName("MeterReplacementSummary")
        .RequireAuthorization();

        // ---------------------------------------------------------------- billing holds
        app.MapGet("/api/v1/meter-data/billing-holds/search", async (
            string? q, bool? activeOnly, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPage);
            var query =
                from h in db.MeterBillingControls.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on h.ConsumerId equals c.Id
                join m in db.Meters.AsNoTracking() on h.MeterId equals m.Id
                select new { h, c, m };

            if (activeOnly ?? true) query = query.Where(x => x.h.ActualBillingBlocked);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = Terms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.m.MeterNumber, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                if (!TryCursor(after, out var at, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
                query = query.Where(x => x.h.BlockedAt < at || (x.h.BlockedAt == at && x.h.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.h.BlockedAt).ThenByDescending(x => x.h.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.h.Id, x.h.MeterId, x.c.AccountNumber, x.c.Name, x.m.MeterNumber, x.h.ActualBillingBlocked, x.h.BlockReason, x.h.BlockedAt, x.h.ClearedAt,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? Cursor(items[^1].BlockedAt, items[^1].Id) : null, totalCount });
        })
        .WithName("SearchBillingHolds")
        .RequireAuthorization();

        app.MapGet("/api/v1/meter-data/billing-holds/summary", async (PrepaidEngineDbContext db) =>
        {
            var active = await db.MeterBillingControls.AsNoTracking().CountAsync(c => c.ActualBillingBlocked);
            var total = await db.MeterBillingControls.AsNoTracking().CountAsync();
            return Results.Ok(new { Total = total, Active = active, Cleared = total - active });
        })
        .WithName("BillingHoldSummary")
        .RequireAuthorization();
    }
}
