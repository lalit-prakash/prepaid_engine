using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Reports;

/// <summary>
/// Every report endpoint filters and aggregates in the database, and returns { rows, truncated, generatedAt }
/// (plus totals where meaningful). Detail reports are capped at <see cref="RowCap"/> rows and say so via
/// <c>truncated</c>; a full-population export needs the background report-job model, which does not exist yet,
/// rather than loading unbounded rows.
///
/// Network hierarchy: every report accepts zoneId, circleId, divisionId, subDivisionId, substationId, feederId
/// and dtrId to narrow to one part of the network. Row-level reports show the consumer's zone, circle, division,
/// sub-division, substation, feeder and DTR on every row; the day-wise reports take <c>level</c>
/// (zone|circle|division|subdivision|substation|feeder|dtr) and then break each day down by that level.
/// </summary>
public static class ReportEndpoints
{
    private const int RowCap = 5000;

    private static (DateTime? From, DateTime? ToExclusive) Range(DateTime? from, DateTime? to) =>
        (from.HasValue ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) : null,
         to.HasValue ? DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc) : null);

    public static void MapReportEndpoints(this WebApplication app)
    {
        // Day-wise RC/DC report: connectivity command counts per calendar day (and per hierarchy level when asked).
        app.MapGet("/api/v1/reports/day-wise-rc-dc", async (PrepaidEngineDbContext db, DateTime? from, DateTime? to, string? level, [AsParameters] HierarchyQuery h) =>
        {
            if (level is not null && !Hierarchy.IsLevel(level)) return Results.BadRequest(new { error = "Unknown level." });
            var (start, endExclusive) = Range(from, to);
            var keyed = db.Consumers.AsNoTracking().ByHierarchy(h).Select(Hierarchy.KeyFor(level));
            var query = from c in db.ConnectivityCommands.AsNoTracking()
                        join k in keyed on c.ConsumerId equals k.Id
                        select new { c, k.Key };
            if (start.HasValue) query = query.Where(x => x.c.CreatedAt >= start.Value);
            if (endExclusive.HasValue) query = query.Where(x => x.c.CreatedAt < endExclusive.Value);

            var rows = await query
                .GroupBy(x => new { x.c.CreatedAt.Date, x.Key })
                .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Key)
                .Take(RowCap)
                .Select(g => new
                {
                    Date = g.Key.Date,
                    Group = g.Key.Key,
                    DisconnectCount = g.Count(x => x.c.CommandType == ConnectivityCommandType.Disconnect),
                    ReconnectCount = g.Count(x => x.c.CommandType == ConnectivityCommandType.Reconnect),
                    AcknowledgedCount = g.Count(x => x.c.Status == ConnectivityCommandStatus.Acknowledged),
                    FailedCount = g.Count(x => x.c.Status == ConnectivityCommandStatus.Failed),
                    TimedOutCount = g.Count(x => x.c.Status == ConnectivityCommandStatus.TimedOut),
                    TotalCount = g.Count(),
                })
                .ToListAsync();

            return Results.Ok(new { rows, truncated = rows.Count >= RowCap, generatedAt = DateTime.UtcNow });
        })
        .WithName("DayWiseRcDcReport")
        .RequireAuthorization();

        // Day-wise recharge summary. Payment outcome and meter-credit outcome stay separate.
        app.MapGet("/api/v1/reports/day-wise-recharge", async (PrepaidEngineDbContext db, DateTime? from, DateTime? to, string? level, [AsParameters] HierarchyQuery h) =>
        {
            if (level is not null && !Hierarchy.IsLevel(level)) return Results.BadRequest(new { error = "Unknown level." });
            var (start, endExclusive) = Range(from, to);
            var keyed = db.Consumers.AsNoTracking().ByHierarchy(h).Select(Hierarchy.KeyFor(level));
            var query =
                from r in db.RechargeTransactions.AsNoTracking()
                join k in keyed on r.ConsumerId equals k.Id
                join mcOuter in db.MeterCommands.AsNoTracking() on r.Id equals mcOuter.RechargeTransactionId into mcGroup
                from mc in mcGroup.DefaultIfEmpty()
                select new { r, mc, k.Key };
            if (start.HasValue) query = query.Where(x => x.r.InitiatedAt >= start.Value);
            if (endExclusive.HasValue) query = query.Where(x => x.r.InitiatedAt < endExclusive.Value);

            var rows = await query
                .GroupBy(x => new { x.r.InitiatedAt.Date, x.Key })
                .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Key)
                .Take(RowCap)
                .Select(g => new
                {
                    Date = g.Key.Date,
                    Group = g.Key.Key,
                    TotalCount = g.Count(),
                    PaymentReceivedCount = g.Count(x => x.r.Status == RechargeStatus.Success),
                    PaymentFailedCount = g.Count(x => x.r.Status == RechargeStatus.Failed),
                    PaymentPendingCount = g.Count(x => x.r.Status == RechargeStatus.Initiated),
                    MeterCreditedCount = g.Count(x => x.mc != null && x.mc.Status == MeterCommandStatus.Acknowledged),
                    MeterCreditFailedCount = g.Count(x => x.mc != null && (x.mc.Status == MeterCommandStatus.Failed || x.mc.Status == MeterCommandStatus.TimedOut)),
                    AmountReceived = g.Where(x => x.r.Status == RechargeStatus.Success).Sum(x => x.r.Amount),
                })
                .ToListAsync();

            return Results.Ok(new
            {
                rows,
                truncated = rows.Count >= RowCap,
                generatedAt = DateTime.UtcNow,
                totals = new
                {
                    TotalCount = rows.Sum(x => x.TotalCount),
                    PaymentReceivedCount = rows.Sum(x => x.PaymentReceivedCount),
                    PaymentFailedCount = rows.Sum(x => x.PaymentFailedCount),
                    MeterCreditedCount = rows.Sum(x => x.MeterCreditedCount),
                    MeterCreditFailedCount = rows.Sum(x => x.MeterCreditFailedCount),
                    AmountReceived = rows.Sum(x => x.AmountReceived),
                },
            });
        })
        .WithName("DayWiseRechargeReport")
        .RequireAuthorization();

        // Meter Credit Failure Report: every MeterCommand that is Failed or TimedOut.
        app.MapGet("/api/v1/reports/meter-credit-failures", async (PrepaidEngineDbContext db, DateTime? from, DateTime? to, [AsParameters] HierarchyQuery h) =>
        {
            var (start, endExclusive) = Range(from, to);
            var query =
                from m in db.MeterCommands.AsNoTracking()
                join c in db.Consumers.AsNoTracking().ByHierarchy(h) on m.ConsumerId equals c.Id
                where m.Status == MeterCommandStatus.Failed || m.Status == MeterCommandStatus.TimedOut
                select new { m, c };
            if (start.HasValue) query = query.Where(x => x.m.CreatedAt >= start.Value);
            if (endExclusive.HasValue) query = query.Where(x => x.m.CreatedAt < endExclusive.Value);

            var rows = await query
                .OrderByDescending(x => x.m.CreatedAt)
                .Take(RowCap + 1)
                .Select(x => new
                {
                    x.m.Id,
                    x.c.AccountNumber,
                    x.c.Name,
                    Zone = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Zone.Name,
                    Circle = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Name,
                    Division = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Name,
                    SubDivision = x.c.Dtr!.Feeder.Substation.SubDivision.Name,
                    Substation = x.c.Dtr!.Feeder.Substation.Name,
                    Feeder = x.c.Dtr!.Feeder.Name,
                    FeederCode = x.c.Dtr!.Feeder.Code,
                    Dtr = x.c.Dtr!.Name,
                    DtrCode = x.c.Dtr!.Code,
                    x.m.CreditAmount,
                    x.m.Status,
                    x.m.RetryCount,
                    x.m.ErrorMessage,
                    x.m.ResponseCode,
                    x.m.CreatedAt,
                })
                .ToListAsync();

            var truncated = rows.Count > RowCap;
            return Results.Ok(new { rows = truncated ? rows.Take(RowCap).ToList() : rows, truncated, generatedAt = DateTime.UtcNow });
        })
        .WithName("MeterCreditFailureReport")
        .RequireAuthorization();

        // Recharge Failure Report: every RechargeTransaction that Failed.
        app.MapGet("/api/v1/reports/recharge-failures", async (PrepaidEngineDbContext db, DateTime? from, DateTime? to, [AsParameters] HierarchyQuery h) =>
        {
            var (start, endExclusive) = Range(from, to);
            var query =
                from r in db.RechargeTransactions.AsNoTracking()
                join c in db.Consumers.AsNoTracking().ByHierarchy(h) on r.ConsumerId equals c.Id
                where r.Status == RechargeStatus.Failed
                select new { r, c };
            if (start.HasValue) query = query.Where(x => x.r.InitiatedAt >= start.Value);
            if (endExclusive.HasValue) query = query.Where(x => x.r.InitiatedAt < endExclusive.Value);

            var rows = await query
                .OrderByDescending(x => x.r.InitiatedAt)
                .Take(RowCap + 1)
                .Select(x => new
                {
                    x.r.Id,
                    x.c.AccountNumber,
                    x.c.Name,
                    Zone = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Zone.Name,
                    Circle = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Name,
                    Division = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Name,
                    SubDivision = x.c.Dtr!.Feeder.Substation.SubDivision.Name,
                    Substation = x.c.Dtr!.Feeder.Substation.Name,
                    Feeder = x.c.Dtr!.Feeder.Name,
                    FeederCode = x.c.Dtr!.Feeder.Code,
                    Dtr = x.c.Dtr!.Name,
                    DtrCode = x.c.Dtr!.Code,
                    x.r.Amount,
                    x.r.RmsReferenceId,
                    x.r.Status,
                    x.r.InitiatedAt,
                })
                .ToListAsync();

            var truncated = rows.Count > RowCap;
            return Results.Ok(new { rows = truncated ? rows.Take(RowCap).ToList() : rows, truncated, generatedAt = DateTime.UtcNow });
        })
        .WithName("RechargeFailureReport")
        .RequireAuthorization();

        // Daily Billing Report: bills in a date range with their full charge breakdown, plus totals computed over
        // the whole filtered set in the database (not just the capped rows).
        app.MapGet("/api/v1/reports/billing", async (PrepaidEngineDbContext db, DateTime? from, DateTime? to, BillStatus? status, [AsParameters] HierarchyQuery h) =>
        {
            var (start, endExclusive) = Range(from, to);
            var query =
                from b in db.Bills.AsNoTracking()
                join c in db.Consumers.AsNoTracking().ByHierarchy(h) on b.ConsumerId equals c.Id
                join t in db.Tariffs.AsNoTracking() on b.TariffId equals t.Id
                select new { b, c, t };
            if (start.HasValue) query = query.Where(x => x.b.GeneratedAt >= start.Value);
            if (endExclusive.HasValue) query = query.Where(x => x.b.GeneratedAt < endExclusive.Value);
            if (status.HasValue) query = query.Where(x => x.b.Status == status.Value);

            var totals = await query
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    BillCount = g.Count(),
                    ConsumerCount = g.Select(x => x.c.Id).Distinct().Count(),
                    TotalBilled = g.Sum(x => x.b.Amount),
                    TotalSettled = g.Sum(x => x.b.AmountPaid),
                })
                .FirstOrDefaultAsync();

            var rows = await query
                .OrderByDescending(x => x.b.GeneratedAt)
                .Take(RowCap)
                .Select(x => new
                {
                    x.b.Id,
                    x.b.GeneratedAt,
                    x.c.AccountNumber,
                    x.c.Name,
                    Zone = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Zone.Name,
                    Circle = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Name,
                    Division = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Name,
                    SubDivision = x.c.Dtr!.Feeder.Substation.SubDivision.Name,
                    Substation = x.c.Dtr!.Feeder.Substation.Name,
                    Feeder = x.c.Dtr!.Feeder.Name,
                    FeederCode = x.c.Dtr!.Feeder.Code,
                    Dtr = x.c.Dtr!.Name,
                    DtrCode = x.c.Dtr!.Code,
                    Category = x.t.Category,
                    TariffName = x.t.Name,
                    EnergyChargeNet = x.b.EnergyChargeGross - x.b.PrepaidRebateAmount,
                    x.b.FixedCharge,
                    x.b.ElectricityDutyAmount,
                    x.b.FppasAmount,
                    x.b.Amount,
                    x.b.AmountPaid,
                    x.b.Status,
                })
                .ToListAsync();

            return Results.Ok(new
            {
                rows,
                truncated = (totals?.BillCount ?? 0) > rows.Count,
                generatedAt = DateTime.UtcNow,
                totals = new
                {
                    BillCount = totals?.BillCount ?? 0,
                    ConsumerCount = totals?.ConsumerCount ?? 0,
                    TotalBilled = totals?.TotalBilled ?? 0m,
                    TotalSettled = totals?.TotalSettled ?? 0m,
                },
            });
        })
        .WithName("BillingReport")
        .RequireAuthorization();
    }
}
