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

/// <summary>Analytics endpoints, moved out of Program.cs unchanged.</summary>
public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this WebApplication app)
    {
        // --- Analytics ----------------------------------------------------------------------------------
        // One response of database-side aggregates for the Analytics screen. Every series is grouped and
        // summed in Postgres over a bounded date range (default: last 30 days, max 366), never by loading
        // rows. Only what the data model genuinely supports is here: there is no circle/division/feeder,
        // no abnormal-consumption detection and no historical balance snapshots, so those analyses are absent.
        app.MapGet("/api/v1/analytics/overview", async (PrepaidEngineDbContext db, DateTime? from, DateTime? to) =>
        {
            var toDay = (to ?? DateTime.UtcNow).Date;
            var fromDay = (from ?? toDay.AddDays(-29)).Date;
            if (fromDay > toDay)
                return Results.BadRequest(new { error = "'from' must not be after 'to'." });
            if ((toDay - fromDay).TotalDays > 366)
                return Results.BadRequest(new { error = "The date range may not exceed 366 days." });

            var start = DateTime.SpecifyKind(fromDay, DateTimeKind.Utc);
            var endExclusive = DateTime.SpecifyKind(toDay.AddDays(1), DateTimeKind.Utc);
            var fromDate = DateOnly.FromDateTime(fromDay);
            var toDate = DateOnly.FromDateTime(toDay);

            var consumption = await db.DailyLoadProfiles.AsNoTracking()
                .Where(d => d.ProfileDate >= fromDate && d.ProfileDate <= toDate)
                .GroupBy(d => d.ProfileDate)
                .OrderBy(g => g.Key)
                .Select(g => new { Date = g.Key, TotalKwh = g.Sum(d => d.TotalKwh), MeterCount = g.Select(d => d.MeterId).Distinct().Count() })
                .ToListAsync();

            var recharges = await db.RechargeTransactions.AsNoTracking()
                .Where(r => r.InitiatedAt >= start && r.InitiatedAt < endExclusive)
                .GroupBy(r => r.InitiatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Date = g.Key,
                    Attempts = g.Count(),
                    AmountReceived = g.Where(r => r.Status == RechargeStatus.Success).Sum(r => r.Amount),
                    Failed = g.Count(r => r.Status == RechargeStatus.Failed),
                })
                .ToListAsync();

            var billing = await db.Bills.AsNoTracking()
                .Where(b => b.GeneratedAt >= start && b.GeneratedAt < endExclusive)
                .GroupBy(b => b.GeneratedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new { Date = g.Key, BillCount = g.Count(), Billed = g.Sum(b => b.Amount), Settled = g.Sum(b => b.AmountPaid) })
                .ToListAsync();

            var commands = await db.ConnectivityCommands.AsNoTracking()
                .Where(c => c.CreatedAt >= start && c.CreatedAt < endExclusive)
                .GroupBy(c => c.CreatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Date = g.Key,
                    Disconnects = g.Count(c => c.CommandType == ConnectivityCommandType.Disconnect),
                    Reconnects = g.Count(c => c.CommandType == ConnectivityCommandType.Reconnect),
                })
                .ToListAsync();

            var communication = await db.MeterEvents.AsNoTracking()
                .Where(e => e.EventTimestamp >= start && e.EventTimestamp < endExclusive
                    && (e.EventCode == MeterEventCode.CommunicationFailure || e.EventCode == MeterEventCode.CommunicationRestoration))
                .GroupBy(e => e.EventTimestamp.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Date = g.Key,
                    Failures = g.Count(e => e.EventCode == MeterEventCode.CommunicationFailure),
                    Restorations = g.Count(e => e.EventCode == MeterEventCode.CommunicationRestoration),
                })
                .ToListAsync();

            // Point-in-time (not date-ranged): where wallet balances sit right now.
            var walletBands = await db.Wallets.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Overdrawn = g.Count(w => w.Balance < 0),
                    UpTo100 = g.Count(w => w.Balance >= 0 && w.Balance < 100),
                    UpTo500 = g.Count(w => w.Balance >= 100 && w.Balance < 500),
                    UpTo1000 = g.Count(w => w.Balance >= 500 && w.Balance < 1000),
                    UpTo5000 = g.Count(w => w.Balance >= 1000 && w.Balance < 5000),
                    Over5000 = g.Count(w => w.Balance >= 5000),
                })
                .FirstOrDefaultAsync();

            var tariffMix = await (
                from b in db.Bills.AsNoTracking()
                join t in db.Tariffs.AsNoTracking() on b.TariffId equals t.Id
                where b.GeneratedAt >= start && b.GeneratedAt < endExclusive
                group b by new { t.Id, t.Name, t.Category, t.Status } into g
                orderby g.Sum(x => x.Amount) descending
                select new { TariffId = g.Key.Id, TariffName = g.Key.Name, g.Key.Category, TariffStatus = g.Key.Status, BillCount = g.Count(), Billed = g.Sum(x => x.Amount) })
                .Take(20)
                .ToListAsync();

            var exceptions = await db.OperationalExceptions.AsNoTracking()
                .Where(e => e.CreatedAt >= start && e.CreatedAt < endExclusive)
                .GroupBy(e => new { e.SourceType, e.Status })
                .Select(g => new { g.Key.SourceType, g.Key.Status, Count = g.Count() })
                .ToListAsync();

            var totalBilled = billing.Sum(b => b.Billed);
            var totalSettled = billing.Sum(b => b.Settled);

            return Results.Ok(new
            {
                From = fromDay,
                To = toDay,
                GeneratedAt = DateTime.UtcNow,
                Totals = new
                {
                    ConsumptionKwh = consumption.Sum(c => c.TotalKwh),
                    RechargeAttempts = recharges.Sum(r => r.Attempts),
                    RechargeReceived = recharges.Sum(r => r.AmountReceived),
                    RechargeFailed = recharges.Sum(r => r.Failed),
                    Bills = billing.Sum(b => b.BillCount),
                    Billed = totalBilled,
                    Settled = totalSettled,
                    CollectionRatePercent = totalBilled > 0 ? Math.Round(totalSettled / totalBilled * 100m, 1) : (decimal?)null,
                },
                Consumption = consumption,
                Recharges = recharges,
                Billing = billing,
                Commands = commands,
                Communication = communication,
                WalletDistribution = walletBands ?? new { Overdrawn = 0, UpTo100 = 0, UpTo500 = 0, UpTo1000 = 0, UpTo5000 = 0, Over5000 = 0 },
                TariffMix = tariffMix,
                Exceptions = exceptions,
            });
        })
        .WithName("GetAnalyticsOverview")
        .RequireAuthorization();

        // Balance history: the daily wallet totals recorded by WalletStatsWorker, oldest first. Only days that were
        // recorded appear; nothing is back-filled or interpolated.
        app.MapGet("/api/v1/analytics/balance-history", async (DateTime? from, DateTime? to, PrepaidEngineDbContext db) =>
        {
            var end = DateOnly.FromDateTime((to ?? DateTime.UtcNow).Date);
            var start = DateOnly.FromDateTime((from ?? end.ToDateTime(TimeOnly.MinValue).AddDays(-29)).Date);
            if (start > end) return Results.BadRequest(new { error = "The start date must not be after the end date." });
            if (end.DayNumber - start.DayNumber > 365) return Results.BadRequest(new { error = "The date range can be at most 366 days." });

            var rows = await db.DailyWalletStats.AsNoTracking()
                .Where(s => s.Date >= start && s.Date <= end)
                .OrderBy(s => s.Date)
                .Select(s => new { s.Date, s.TotalConsumers, s.ActiveConsumers, s.DisconnectedConsumers, s.LowBalanceConsumers, s.WalletTotal, s.RecordedAt })
                .ToListAsync();
            return Results.Ok(new { From = start, To = end, Rows = rows });
        })
        .WithName("BalanceHistory")
        .RequireAuthorization();
    }
}
