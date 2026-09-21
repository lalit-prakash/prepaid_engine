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

/// <summary>MeterData endpoints, moved out of Program.cs unchanged.</summary>
public static class MeterDataEndpoints
{
    public static void MapMeterDataEndpoints(this WebApplication app)
    {
        // --- DLP billing pipeline: Daily Load Profile ingestion and the daily charge — see -------------
        // IBillingEngineService's doc comment. The Load Survey (LS) hourly pipeline that used to also
        // live in this section has been removed; DLP alone drives ongoing prepaid billing now.
        app.MapPost("/api/v1/meter-data/dlp", async (DailyLoadProfileIngestRequest request, IBillingEngineService billingEngine) =>
        {
            var result = await billingEngine.IngestDailyLoadProfileAsync(
                new DailyLoadProfileRequest(request.ConsumerId, request.MeterId, request.ProfileDate, request.GeneratedAt,
                    request.StartCumulativeKwh, request.EndCumulativeKwh, request.SourceReference, request.StartCumulativeKvah, request.EndCumulativeKvah));

            return Results.Ok(result);
        })
        .WithName("IngestDailyLoadProfile")
        .RequireAuthorization("DataAdmin");


        app.MapGet("/api/v1/meter-data/dlp/search", async (
            string? q, DateTime? from, DateTime? to, DailyProfileStatus? status, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            if (!TryParseCursor(after, out var key, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });

            var query =
                from d in db.DailyLoadProfiles.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on d.ConsumerId equals c.Id
                join m in db.Meters.AsNoTracking() on d.MeterId equals m.Id
                select new { d, c, m };
            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = MeterDataTerms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.m.MeterNumber, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }
            if (from.HasValue) { var f = DateOnly.FromDateTime(from.Value); query = query.Where(x => x.d.ProfileDate >= f); }
            if (to.HasValue) { var t = DateOnly.FromDateTime(to.Value); query = query.Where(x => x.d.ProfileDate <= t); }
            if (status.HasValue) query = query.Where(x => x.d.Status == status.Value);

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                var afterDate = DateOnly.FromDayNumber((int)key);
                query = query.Where(x => x.d.ProfileDate < afterDate || (x.d.ProfileDate == afterDate && x.d.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.d.ProfileDate).ThenByDescending(x => x.d.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.d.Id, x.c.AccountNumber, x.c.Name, x.m.MeterNumber, x.d.ProfileDate, x.d.GeneratedAt, x.d.ReceivedAt,
                    x.d.StartCumulativeKwh, x.d.EndCumulativeKwh, x.d.TotalKwh, x.d.Status, x.d.IsProvisional, x.d.SourceReference,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? $"{items[^1].ProfileDate.DayNumber}_{items[^1].Id}" : null, totalCount });
        })
        .WithName("SearchDailyLoadProfiles")
        .RequireAuthorization();


        app.MapGet("/api/v1/meter-data/bp/search", async (
            string? q, DateTime? from, DateTime? to, RegisterReadingStatus? status, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            if (!TryParseCursor(after, out var key, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
            var (start, endExclusive) = MeterDataRange(from, to);

            var query =
                from r in db.RegisterReadings.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on r.ConsumerId equals c.Id
                join m in db.Meters.AsNoTracking() on r.MeterId equals m.Id
                select new { r, c, m };
            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = MeterDataTerms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.m.MeterNumber, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }
            if (start.HasValue) query = query.Where(x => x.r.ReadingTimestamp >= start.Value);
            if (endExclusive.HasValue) query = query.Where(x => x.r.ReadingTimestamp < endExclusive.Value);
            if (status.HasValue) query = query.Where(x => x.r.Status == status.Value);

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                var afterAt = new DateTime(key, DateTimeKind.Utc);
                query = query.Where(x => x.r.ReadingTimestamp < afterAt || (x.r.ReadingTimestamp == afterAt && x.r.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.r.ReadingTimestamp).ThenByDescending(x => x.r.Id)
                .Take(size + 1)
                .Select(x => new { x.r.Id, x.c.AccountNumber, x.c.Name, x.m.MeterNumber, x.r.ReadingTimestamp, x.r.CumulativeImportKwh, x.r.Status, x.r.ReceivedAt, x.r.SourceReference })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? $"{items[^1].ReadingTimestamp.Ticks}_{items[^1].Id}" : null, totalCount });
        })
        .WithName("SearchRegisterReadings")
        .RequireAuthorization();


        app.MapGet("/api/v1/meter-data/ls/search", async (
            string? q, DateTime? from, DateTime? to, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            if (!TryParseCursor(after, out var key, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
            var (start, endExclusive) = MeterDataRange(from, to);

            var query =
                from l in db.LoadSurveyIntervals.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on l.ConsumerId equals c.Id
                join m in db.Meters.AsNoTracking() on l.MeterId equals m.Id
                select new { l, c, m };
            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = MeterDataTerms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.m.MeterNumber, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }
            if (start.HasValue) query = query.Where(x => x.l.IntervalStart >= start.Value);
            if (endExclusive.HasValue) query = query.Where(x => x.l.IntervalStart < endExclusive.Value);

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                var afterAt = new DateTime(key, DateTimeKind.Utc);
                query = query.Where(x => x.l.IntervalStart < afterAt || (x.l.IntervalStart == afterAt && x.l.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.l.IntervalStart).ThenByDescending(x => x.l.Id)
                .Take(size + 1)
                .Select(x => new { x.l.Id, x.c.AccountNumber, x.c.Name, x.m.MeterNumber, x.l.IntervalStart, x.l.IntervalEnd, x.l.ImportKwh, x.l.SourceReference })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? $"{items[^1].IntervalStart.Ticks}_{items[^1].Id}" : null, totalCount });
        })
        .WithName("SearchLoadSurveyIntervals")
        .RequireAuthorization();


        app.MapGet("/api/v1/meter-data/events/search", async (
            string? q, DateTime? from, DateTime? to, MeterEventCode? eventCode, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            if (!TryParseCursor(after, out var key, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
            var (start, endExclusive) = MeterDataRange(from, to);

            var query =
                from e in db.MeterEvents.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on e.ConsumerId equals c.Id
                join m in db.Meters.AsNoTracking() on e.MeterId equals m.Id
                select new { e, c, m };
            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = MeterDataTerms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.m.MeterNumber, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }
            if (start.HasValue) query = query.Where(x => x.e.EventTimestamp >= start.Value);
            if (endExclusive.HasValue) query = query.Where(x => x.e.EventTimestamp < endExclusive.Value);
            if (eventCode.HasValue) query = query.Where(x => x.e.EventCode == eventCode.Value);

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                var afterAt = new DateTime(key, DateTimeKind.Utc);
                query = query.Where(x => x.e.EventTimestamp < afterAt || (x.e.EventTimestamp == afterAt && x.e.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.e.EventTimestamp).ThenByDescending(x => x.e.Id)
                .Take(size + 1)
                .Select(x => new { x.e.Id, x.c.AccountNumber, x.c.Name, x.m.MeterNumber, x.e.EventCode, x.e.EventTimestamp, x.e.Description, x.e.Status })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? $"{items[^1].EventTimestamp.Ticks}_{items[^1].Id}" : null, totalCount });
        })
        .WithName("SearchMeterEvents")
        .RequireAuthorization();


        app.MapGet("/api/v1/meter-data/alarms/search", async (
            string? q, DateTime? from, DateTime? to, MeterAlarmStatus? status, MeterAlarmSeverity? severity, string? after, int? pageSize,
            PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);
            if (!TryParseCursor(after, out var key, out var afterId)) return Results.BadRequest(new { error = "Invalid cursor." });
            var (start, endExclusive) = MeterDataRange(from, to);

            var query =
                from a in db.MeterAlarms.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on a.ConsumerId equals c.Id
                join m in db.Meters.AsNoTracking() on a.MeterId equals m.Id
                select new { a, c, m };
            if (!string.IsNullOrWhiteSpace(q))
            {
                var (prefix, contains) = MeterDataTerms(q);
                query = query.Where(x => EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") || EF.Functions.ILike(x.m.MeterNumber, prefix, "\\") || EF.Functions.ILike(x.c.Name, contains, "\\"));
            }
            if (start.HasValue) query = query.Where(x => x.a.RaisedAt >= start.Value);
            if (endExclusive.HasValue) query = query.Where(x => x.a.RaisedAt < endExclusive.Value);
            if (status.HasValue) query = query.Where(x => x.a.Status == status.Value);
            if (severity.HasValue) query = query.Where(x => x.a.Severity == severity.Value);

            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after))
            {
                var afterAt = new DateTime(key, DateTimeKind.Utc);
                query = query.Where(x => x.a.RaisedAt < afterAt || (x.a.RaisedAt == afterAt && x.a.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.a.RaisedAt).ThenByDescending(x => x.a.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.a.Id, x.c.AccountNumber, x.c.Name, x.m.MeterNumber, x.a.AlarmCode, x.a.Severity, x.a.RaisedAt,
                    x.a.Status, x.a.AcknowledgedAt, x.a.AcknowledgedBy, x.a.ResolvedAt, x.a.ResolutionNote,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? $"{items[^1].RaisedAt.Ticks}_{items[^1].Id}" : null, totalCount });
        })
        .WithName("SearchMeterAlarms")
        .RequireAuthorization();


        app.MapGet("/api/v1/meter-data/dlp", async (HttpContext http, Guid? consumerId, PrepaidEngineDbContext db) =>
        {
            var query = db.DailyLoadProfiles.AsQueryable();
            if (consumerId.HasValue)
                query = query.Where(d => d.ConsumerId == consumerId.Value);

            var profiles = await (
                from d in query
                join consumer in db.Consumers on d.ConsumerId equals consumer.Id
                join meter in db.Meters on d.MeterId equals meter.Id
                orderby d.ProfileDate descending
                select new
                {
                    d.Id,
                    consumer.AccountNumber,
                    consumer.Name,
                    meter.MeterNumber,
                    d.ProfileDate,
                    d.GeneratedAt,
                    d.ReceivedAt,
                    d.StartCumulativeKwh,
                    d.EndCumulativeKwh,
                    d.TotalKwh,
                    d.Status,
                    d.IsProvisional,
                    d.SourceReference,
                })
                .Take(500)
                .ToCappedListAsync(http);

            return Results.Ok(profiles);
        })
        .WithName("ListDailyLoadProfiles")
        .RequireAuthorization();


        // --- MDMS data foundation: BP (register validation), LS (consumption intelligence), IP
        // (instantaneous meter health), Events/Alarms, and cross-source energy validation. None of these
        // bill anything — DLP above remains the sole daily billing driver. See
        // IMeterDataIngestionService's doc comment for each source's role.

        app.MapPost("/api/v1/meter-data/bp", async (RegisterReadingIngestRequest request, IMeterDataIngestionService meterData) =>
        {
            var result = await meterData.IngestRegisterReadingAsync(
                new RegisterReadingRequest(request.ConsumerId, request.MeterId, request.ReadingTimestamp, request.CumulativeImportKwh, request.SourceReference));
            return Results.Ok(result);
        })
        .WithName("IngestRegisterReading")
        .RequireAuthorization("DataAdmin");


        app.MapGet("/api/v1/meter-data/bp", async (HttpContext http, Guid? consumerId, Guid? meterId, PrepaidEngineDbContext db) =>
        {
            var query = db.RegisterReadings.AsQueryable();
            if (consumerId.HasValue) query = query.Where(r => r.ConsumerId == consumerId.Value);
            if (meterId.HasValue) query = query.Where(r => r.MeterId == meterId.Value);

            var readings = await (
                from r in query
                join consumer in db.Consumers on r.ConsumerId equals consumer.Id
                join meter in db.Meters on r.MeterId equals meter.Id
                orderby r.ReadingTimestamp descending
                select new { r.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, r.ReadingTimestamp, r.CumulativeImportKwh, r.Status, r.ReceivedAt, r.SourceReference })
                .Take(500)
                .ToCappedListAsync(http);

            return Results.Ok(readings);
        })
        .WithName("ListRegisterReadings")
        .RequireAuthorization();


        app.MapPost("/api/v1/meter-data/ls", async (LoadSurveyIntervalIngestRequest request, IMeterDataIngestionService meterData) =>
        {
            var result = await meterData.IngestLoadSurveyIntervalAsync(
                new LoadSurveyIntervalRequest(request.ConsumerId, request.MeterId, request.IntervalStart, request.IntervalEnd, request.ImportKwh, request.SourceReference, request.ImportKvah));
            return Results.Ok(result);
        })
        .WithName("IngestLoadSurveyInterval")
        .RequireAuthorization("DataAdmin");


        app.MapGet("/api/v1/meter-data/ls", async (HttpContext http, Guid? consumerId, Guid? meterId, DateTime? from, DateTime? to, PrepaidEngineDbContext db) =>
        {
            var query = db.LoadSurveyIntervals.AsQueryable();
            if (consumerId.HasValue) query = query.Where(l => l.ConsumerId == consumerId.Value);
            if (meterId.HasValue) query = query.Where(l => l.MeterId == meterId.Value);
            if (from.HasValue) query = query.Where(l => l.IntervalStart >= from.Value);
            if (to.HasValue) query = query.Where(l => l.IntervalStart < to.Value);

            var intervals = await (
                from l in query
                join consumer in db.Consumers on l.ConsumerId equals consumer.Id
                join meter in db.Meters on l.MeterId equals meter.Id
                orderby l.IntervalStart descending
                select new { l.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, l.IntervalStart, l.IntervalEnd, l.ImportKwh, l.SourceReference })
                .Take(1000)
                .ToCappedListAsync(http);

            return Results.Ok(intervals);
        })
        .WithName("ListLoadSurveyIntervals")
        .RequireAuthorization();


        app.MapPost("/api/v1/meter-data/ip", async (InstantaneousReadingIngestRequest request, IMeterDataIngestionService meterData) =>
        {
            var result = await meterData.IngestInstantaneousReadingAsync(
                new InstantaneousReadingRequest(request.ConsumerId, request.MeterId, request.Timestamp, request.VoltageVolts,
                    request.CurrentAmps, request.PowerKw, request.PowerFactor, request.FrequencyHz, request.RelayStatus, request.SourceReference));
            return Results.Ok(result);
        })
        .WithName("IngestInstantaneousReading")
        .RequireAuthorization("DataAdmin");


        // Latest IP reading per meter — meter-health snapshot, never a daily-billing input. The
        // group-by-then-take-first step is done as its own query (translates cleanly against the base
        // entity), then joined against Consumers/Meters in memory — EF Core's SQL translator cannot
        // express a three-way join combined with "first row per group" in a single query.
        app.MapGet("/api/v1/meter-data/ip/latest", async (HttpContext http, Guid? consumerId, Guid? meterId, PrepaidEngineDbContext db) =>
        {
            var query = db.InstantaneousReadings.AsQueryable();
            if (consumerId.HasValue) query = query.Where(i => i.ConsumerId == consumerId.Value);
            if (meterId.HasValue) query = query.Where(i => i.MeterId == meterId.Value);

            var latestReadings = await query
                .GroupBy(i => i.MeterId)
                .Select(g => g.OrderByDescending(i => i.Timestamp).First())
                .ToCappedListAsync(http);

            var consumerIds = latestReadings.Select(r => r.ConsumerId).ToHashSet();
            var meterIds = latestReadings.Select(r => r.MeterId).ToHashSet();
            var consumers = await db.Consumers.Where(c => consumerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
            var meters = await db.Meters.Where(m => meterIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

            var latestByMeter = latestReadings.Select(i => new
            {
                i.Id,
                AccountNumber = consumers[i.ConsumerId].AccountNumber,
                consumers[i.ConsumerId].Name,
                MeterNumber = meters[i.MeterId].MeterNumber,
                i.Timestamp,
                i.VoltageVolts, i.CurrentAmps, i.PowerKw, i.PowerFactor, i.FrequencyHz, i.RelayStatus,
            });

            return Results.Ok(latestByMeter);
        })
        .WithName("GetLatestInstantaneousReadings")
        .RequireAuthorization();


        app.MapPost("/api/v1/meter-data/events", async (MeterEventIngestRequest request, IMeterDataIngestionService meterData) =>
        {
            var result = await meterData.IngestMeterEventAsync(
                new MeterEventRequest(request.ConsumerId, request.MeterId, request.EventCode, request.EventTimestamp, request.Description, request.SourceReference));
            return Results.Ok(result);
        })
        .WithName("IngestMeterEvent")
        .RequireAuthorization("DataAdmin");


        app.MapGet("/api/v1/meter-data/events", async (HttpContext http, Guid? consumerId, Guid? meterId, PrepaidEngineDbContext db) =>
        {
            var query = db.MeterEvents.AsQueryable();
            if (consumerId.HasValue) query = query.Where(e => e.ConsumerId == consumerId.Value);
            if (meterId.HasValue) query = query.Where(e => e.MeterId == meterId.Value);

            var events = await (
                from e in query
                join consumer in db.Consumers on e.ConsumerId equals consumer.Id
                join meter in db.Meters on e.MeterId equals meter.Id
                orderby e.EventTimestamp descending
                select new { e.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, e.EventCode, e.EventTimestamp, e.Description, e.Status })
                .Take(500)
                .ToCappedListAsync(http);

            return Results.Ok(events);
        })
        .WithName("ListMeterEvents")
        .RequireAuthorization();


        app.MapPost("/api/v1/meter-data/alarms", async (MeterAlarmIngestRequest request, IMeterDataIngestionService meterData) =>
        {
            var result = await meterData.IngestMeterAlarmAsync(
                new MeterAlarmRequest(request.ConsumerId, request.MeterId, request.AlarmCode, request.Severity, request.RaisedAt, request.SourceReference));
            return Results.Ok(result);
        })
        .WithName("IngestMeterAlarm")
        .RequireAuthorization("DataAdmin");


        app.MapGet("/api/v1/meter-data/alarms", async (HttpContext http, Guid? consumerId, Guid? meterId, MeterAlarmStatus? status, PrepaidEngineDbContext db) =>
        {
            var query = db.MeterAlarms.AsQueryable();
            if (consumerId.HasValue) query = query.Where(a => a.ConsumerId == consumerId.Value);
            if (meterId.HasValue) query = query.Where(a => a.MeterId == meterId.Value);
            if (status.HasValue) query = query.Where(a => a.Status == status.Value);

            var alarms = await (
                from a in query
                join consumer in db.Consumers on a.ConsumerId equals consumer.Id
                join meter in db.Meters on a.MeterId equals meter.Id
                orderby a.RaisedAt descending
                select new
                {
                    a.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, a.AlarmCode, a.Severity, a.RaisedAt,
                    a.Status, a.AcknowledgedAt, a.AcknowledgedBy, a.ResolvedAt, a.ResolutionNote,
                })
                .Take(500)
                .ToCappedListAsync(http);

            return Results.Ok(alarms);
        })
        .WithName("ListMeterAlarms")
        .RequireAuthorization();


        app.MapPost("/api/v1/meter-data/alarms/{id:guid}/acknowledge", async (Guid id, AcknowledgeAlarmRequest request, PrepaidEngineDbContext db, ClaimsPrincipal user) =>
        {
            var alarm = await db.MeterAlarms.FirstOrDefaultAsync(a => a.Id == id);
            if (alarm is null) return Results.NotFound();

            try
            {
                // The acknowledger is the authenticated user; a name supplied in the request body is ignored so it cannot be forged.
                alarm.Acknowledge(user.Identity?.Name ?? "unknown", DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            Audit(db, nameof(MeterAlarm), alarm.Id.ToString(), "ALARM_ACKNOWLEDGED", user.Identity?.Name ?? "unknown");
            await db.SaveChangesAsync();
            return Results.Ok(new { alarm.Id, alarm.Status, alarm.AcknowledgedAt, alarm.AcknowledgedBy });
        })
        .WithName("AcknowledgeMeterAlarm")
        .RequireAuthorization("Operations");


        app.MapPost("/api/v1/meter-data/alarms/{id:guid}/resolve", async (Guid id, ResolveAlarmRequest request, PrepaidEngineDbContext db, ClaimsPrincipal user) =>
        {
            var alarm = await db.MeterAlarms.FirstOrDefaultAsync(a => a.Id == id);
            if (alarm is null) return Results.NotFound();

            try
            {
                alarm.Resolve(request.ResolutionNote, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            Audit(db, nameof(MeterAlarm), alarm.Id.ToString(), "ALARM_RESOLVED", user.Identity?.Name ?? "unknown", details: request.ResolutionNote);
            await db.SaveChangesAsync();
            return Results.Ok(new { alarm.Id, alarm.Status, alarm.ResolvedAt, alarm.ResolutionNote });
        })
        .WithName("ResolveMeterAlarm")
        .RequireAuthorization("Operations");


        // DLP completeness for a given date across every active prepaid consumer — see
        // DlpCompletenessStatus's doc comment for what each status means and how it should influence
        // billing (surfaced operationally; this endpoint itself never blocks or triggers billing).
        app.MapGet("/api/v1/meter-data/dlp-completeness", async (DateOnly date, IMeterDataIngestionService meterData) =>
        {
            var rows = await meterData.GetDlpCompletenessAsync(date);
            return Results.Ok(rows);
        })
        .WithName("GetDlpCompleteness")
        .RequireAuthorization();


        // Cross-source energy validation (BP vs DLP, LS vs DLP, BP vs LS) for one consumer/meter/day —
        // see IMeterDataIngestionService.EvaluateEnergyValidationAsync's doc comment. Evaluated on demand
        // here rather than continuously, since it only makes sense once the day's DLP/BP/LS have arrived.
        app.MapPost("/api/v1/meter-data/energy-validation", async (EvaluateEnergyValidationRequest request, IMeterDataIngestionService meterData) =>
        {
            var outcomes = await meterData.EvaluateEnergyValidationAsync(request.ConsumerId, request.MeterId, request.ValidationDate);
            return Results.Ok(outcomes);
        })
        .WithName("EvaluateEnergyValidation")
        .RequireAuthorization("DataAdmin");


        app.MapGet("/api/v1/meter-data/energy-validation", async (HttpContext http, Guid? consumerId, Guid? meterId, EnergyValidationStatus? status, PrepaidEngineDbContext db) =>
        {
            var query = db.EnergyValidationResults.AsQueryable();
            if (consumerId.HasValue) query = query.Where(v => v.ConsumerId == consumerId.Value);
            if (meterId.HasValue) query = query.Where(v => v.MeterId == meterId.Value);
            if (status.HasValue) query = query.Where(v => v.Status == status.Value);

            var results = await (
                from v in query
                join consumer in db.Consumers on v.ConsumerId equals consumer.Id
                join meter in db.Meters on v.MeterId equals meter.Id
                orderby v.ValidationDate descending
                select new
                {
                    v.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, v.ValidationDate, v.Rule,
                    v.ExpectedValueKwh, v.ActualValueKwh, v.VarianceKwh, v.VariancePct, v.Status, v.Reason, v.EvaluatedAt,
                })
                .Take(500)
                .ToCappedListAsync(http);

            return Results.Ok(results);
        })
        .WithName("ListEnergyValidationResults")
        .RequireAuthorization();


        // Cross-consumer operator view of every recorded meter replacement (spec §15-16) — the audit
        // trail that exists specifically so an old meter's cumulative reading is never compared against
        // a new meter's (they're different physical meters). Old/new meter numbers are resolved via a
        // left join since OldMeterId is null for an initial Installed event (not currently produced by
        // ReplaceMeterAsync, which only ever records Replaced, but the entity/join supports it).
        app.MapGet("/api/v1/meter-replacements", async (HttpContext http, PrepaidEngineDbContext db) =>
        {
            var replacements = await (
                from a in db.MeterAssignments
                join consumer in db.Consumers on a.ConsumerId equals consumer.Id
                join newMeter in db.Meters on a.NewMeterId equals newMeter.Id
                join oldMeter in db.Meters on a.OldMeterId equals oldMeter.Id into oldMeterJoin
                from oldMeter in oldMeterJoin.DefaultIfEmpty()
                orderby a.RecordedAt descending
                select new
                {
                    a.Id,
                    consumer.AccountNumber,
                    consumer.Name,
                    a.EventType,
                    OldMeterNumber = oldMeter != null ? oldMeter.MeterNumber : null,
                    NewMeterNumber = newMeter.MeterNumber,
                    a.EffectiveFrom,
                    a.OldMeterClosingReadingKwh,
                    a.NewMeterOpeningReadingKwh,
                    a.Reason,
                    a.RecordedAt,
                })
                .ToCappedListAsync(http);

            return Results.Ok(replacements);
        })
        .WithName("ListMeterReplacements")
        .RequireAuthorization();


        // Operator visibility into MeterBillingControl holds (spec §8) — without this, the clear
        // endpoint below has nothing for an operator to act against.
        app.MapGet("/api/v1/meter-data/billing-holds", async (HttpContext http, bool? activeOnly, PrepaidEngineDbContext db) =>
        {
            var query = db.MeterBillingControls.AsQueryable();
            if (activeOnly ?? true)
                query = query.Where(c => c.ActualBillingBlocked);

            var holds = await (
                from c in query
                join consumer in db.Consumers on c.ConsumerId equals consumer.Id
                join meter in db.Meters on c.MeterId equals meter.Id
                orderby c.BlockedAt descending
                select new
                {
                    c.Id,
                    c.MeterId,
                    consumer.AccountNumber,
                    consumer.Name,
                    meter.MeterNumber,
                    c.ActualBillingBlocked,
                    c.BlockReason,
                    c.BlockedAt,
                    c.ClearedAt,
                })
                .ToCappedListAsync(http);

            return Results.Ok(holds);
        })
        .WithName("ListMeterBillingHolds")
        .RequireAuthorization();


        // The operator-facing clear API the LS/DLP spec §8 calls for — a documented follow-up when that
        // spec landed, built now. Requires a mandatory resolution note (this project's established
        // mandatory-reason discipline — see ConnectivityCommand.Reason) and leaves an audit trail; actual
        // billing for the meter resumes (both hourly LS and daily DLP) the moment the hold clears.
        app.MapPost("/api/v1/meter-data/{meterId:guid}/billing-hold/clear", async (
            Guid meterId, ResolutionRequest request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Note))
                return Results.BadRequest(new { error = "A resolution note is required to clear a billing hold." });

            var control = await db.MeterBillingControls.FirstOrDefaultAsync(c => c.MeterId == meterId && c.ActualBillingBlocked);
            if (control is null)
                return Results.NotFound(new { error = "No active billing hold exists for this meter." });

            control.Clear(DateTime.UtcNow);

            Audit(db, nameof(MeterBillingControl), control.Id.ToString(), "BillingHoldCleared", user.Identity?.Name ?? "unknown", details: request.Note);
            await db.SaveChangesAsync();

            return Results.Ok(new { control.Id, control.MeterId, control.ActualBillingBlocked, control.ClearedAt });
        })
        .WithName("ClearMeterBillingHold")
        .RequireAuthorization("Operations");


        // Bulk variant of the endpoint above — the same mandatory-reason discipline, one shared
        // resolution note applied to every meter in the batch (an operator clearing several holds at
        // once is asserting one common finding, e.g. "confirmed with field crew: all listed meters were
        // reset during today's maintenance window" — if the reasons genuinely differ per meter, that's a
        // signal to clear them individually with the single-hold endpoint instead, not something this
        // endpoint should paper over with per-item notes). One bad meter ID in the batch does not fail
        // the rest — each is independently validated and reported, matching the batch-processing pattern
        // already used by POST /api/v1/conversions.
        app.MapPost("/api/v1/meter-data/billing-holds/clear-bulk", async (
            BulkClearBillingHoldsRequest request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            if (request.MeterIds is null || request.MeterIds.Count == 0)
                return Results.BadRequest(new { error = "At least one meter ID is required." });
            if (string.IsNullOrWhiteSpace(request.Note))
                return Results.BadRequest(new { error = "A resolution note is required to clear a billing hold." });

            var results = new List<object>();

            foreach (var meterId in request.MeterIds.Distinct())
            {
                var control = await db.MeterBillingControls.FirstOrDefaultAsync(c => c.MeterId == meterId && c.ActualBillingBlocked);
                if (control is null)
                {
                    results.Add(new { MeterId = meterId, Cleared = false, Error = "No active billing hold exists for this meter." });
                    continue;
                }

                control.Clear(DateTime.UtcNow);
                Audit(db, nameof(MeterBillingControl), control.Id.ToString(), "BillingHoldCleared", user.Identity?.Name ?? "unknown", details: request.Note);
                results.Add(new { MeterId = meterId, Cleared = true, Error = (string?)null, control.Id, control.ClearedAt });
            }

            await db.SaveChangesAsync();

            return Results.Ok(results);
        })
        .WithName("ClearMeterBillingHoldsBulk")
        .RequireAuthorization("Operations");
    }
}
