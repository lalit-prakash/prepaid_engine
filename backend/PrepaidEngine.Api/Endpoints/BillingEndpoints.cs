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

/// <summary>Billing endpoints, moved out of Program.cs unchanged.</summary>
public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        // Billing dashboard read endpoints — real data across all consumers, joined with the tariff
        // and consumption reading each bill was generated from. Demo/local-only, same Basic-auth
        // stop-gap as every other endpoint above (see docs/assumptions-and-security.md).
        app.MapGet("/api/v1/bills", async (HttpContext http, PrepaidEngineDbContext db) =>
        {
            var bills = await db.Bills
                .Join(db.Consumers, b => b.ConsumerId, c => c.Id, (b, c) => new { Bill = b, Consumer = c })
                .Join(db.Tariffs, x => x.Bill.TariffId, t => t.Id, (x, t) => new { x.Bill, x.Consumer, Tariff = t })
                .OrderByDescending(x => x.Bill.GeneratedAt)
                .Select(x => new
                {
                    x.Bill.Id,
                    x.Consumer.AccountNumber,
                    x.Consumer.Name,
                    Category = x.Tariff.Category,
                    TariffName = x.Tariff.Name,
                    x.Bill.EnergyChargeGross,
                    x.Bill.PrepaidRebateAmount,
                    EnergyChargeNet = x.Bill.EnergyChargeGross - x.Bill.PrepaidRebateAmount,
                    x.Bill.FixedCharge,
                    x.Bill.ElectricityDutyAmount,
                    x.Bill.FppasAmount,
                    x.Bill.TmcAmount,
                    x.Bill.CpmcAmount,
                    x.Bill.ArrearsAmount,
                    x.Bill.Amount,
                    x.Bill.AmountPaid,
                    x.Bill.Status,
                    x.Bill.GeneratedAt,
                })
                .ToCappedListAsync(http);

            return Results.Ok(bills);
        })
        .WithName("ListBills")
        .RequireAuthorization();


        // Server-side searchable, keyset-paginated bill list for the Billing screen (the unpaginated
        // ListBills above stays for any caller that needs every row). Newest first; the cursor is
        // "<GeneratedAt ticks>_<Id>". Identifier fields match by prefix, tariff and consumer name by substring.
        app.MapGet("/api/v1/bills/search", async (
            string? q, BillStatus? status, DateTime? from, DateTime? to, string? after, int? pageSize,
            PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);

            var query =
                from b in db.Bills.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on b.ConsumerId equals c.Id
                join t in db.Tariffs.AsNoTracking() on b.TariffId equals t.Id
                select new { b, c, t };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                var prefix = term + "%";
                var contains = "%" + term + "%";
                query = query.Where(x =>
                    EF.Functions.ILike(x.c.AccountNumber, prefix) ||
                    EF.Functions.ILike(x.c.Name, contains) ||
                    EF.Functions.ILike(x.t.Name, contains));
            }
            if (status.HasValue)
                query = query.Where(x => x.b.Status == status.Value);
            if (from.HasValue)
                query = query.Where(x => x.b.GeneratedAt >= DateTime.SpecifyKind(from.Value, DateTimeKind.Utc));
            if (to.HasValue)
                query = query.Where(x => x.b.GeneratedAt < DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc));

            var totalCount = await query.CountAsync();

            if (!string.IsNullOrEmpty(after))
            {
                var parts = after.Split('_', 2);
                if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var afterId))
                    return Results.BadRequest(new { error = "Invalid cursor." });
                var afterAt = new DateTime(ticks, DateTimeKind.Utc);
                query = query.Where(x => x.b.GeneratedAt < afterAt || (x.b.GeneratedAt == afterAt && x.b.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.b.GeneratedAt).ThenByDescending(x => x.b.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.b.Id,
                    x.c.AccountNumber,
                    x.c.Name,
                    Category = x.t.Category,
                    TariffName = x.t.Name,
                    TariffId = x.t.Id,
                    EnergyChargeNet = x.b.EnergyChargeGross - x.b.PrepaidRebateAmount,
                    x.b.FixedCharge,
                    x.b.ElectricityDutyAmount,
                    x.b.FppasAmount,
                    x.b.Amount,
                    x.b.AmountPaid,
                    x.b.Status,
                    x.b.GeneratedAt,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            var nextCursor = hasMore ? $"{items[^1].GeneratedAt.Ticks}_{items[^1].Id}" : null;
            return Results.Ok(new { items, nextCursor, totalCount });
        })
        .WithName("SearchBills")
        .RequireAuthorization();


        // Database-side aggregates for the Billing KPI strip.
        app.MapGet("/api/v1/bills/summary", async (PrepaidEngineDbContext db) =>
        {
            var byStatus = await db.Bills.AsNoTracking()
                .GroupBy(b => b.Status)
                .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(b => b.Amount), Paid = g.Sum(b => b.AmountPaid) })
                .ToListAsync();

            int Count(BillStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            var total = byStatus.Sum(x => x.Count);
            var billed = byStatus.Sum(x => x.Amount);
            var paid = byStatus.Sum(x => x.Paid);

            return Results.Ok(new
            {
                Total = total,
                Paid = Count(BillStatus.Paid),
                PartiallyPaid = Count(BillStatus.PartiallyPaid),
                Generated = Count(BillStatus.Generated),
                Overdue = Count(BillStatus.Overdue),
                Cancelled = Count(BillStatus.Cancelled),
                TotalBilled = billed,
                TotalSettled = paid,
                Outstanding = billed - paid,
            });
        })
        .WithName("GetBillSummary")
        .RequireAuthorization();


        app.MapGet("/api/v1/bills/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
        {
            var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id);
            if (bill is null)
                return Results.NotFound();

            var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == bill.ConsumerId);
            var tariff = await db.Tariffs.Include(t => t.Slabs).FirstOrDefaultAsync(t => t.Id == bill.TariffId);
            var reading = await db.ConsumptionReadings.FirstOrDefaultAsync(r => r.Id == bill.ConsumptionReadingId);

            if (consumer is null || tariff is null || reading is null)
            {
                // Data integrity issue (a bill referencing a deleted consumer/tariff/reading) rather
                // than a legitimate "not found" for the bill itself — surface it distinctly.
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Bill references missing data",
                    detail: $"Bill {id} references a consumer, tariff, or consumption reading that no longer exists.");
            }

            // Energy charge split by slab, computed here (never in the browser) from the exact tariff row the
            // bill references. The tariff row is immutable, so this reproduces the historical calculation.
            // If the slab sum does not match the stored gross charge (e.g. a ToD tariff), say so instead of
            // presenting a breakdown that does not explain the bill.
            var slabBreakdown = tariff.Slabs
                .OrderBy(sl => sl.FromKwh)
                .Select(sl =>
                {
                    var upper = sl.UpToKwh ?? reading.ConsumptionKwh;
                    var kwhInSlab = Math.Max(0m, Math.Min(reading.ConsumptionKwh, upper) - sl.FromKwh);
                    return new { sl.FromKwh, sl.UpToKwh, sl.RatePerKwh, KwhInSlab = kwhInSlab, Charge = kwhInSlab * sl.RatePerKwh };
                })
                .ToList();
            var slabBreakdownReconciles = slabBreakdown.Count > 0
                && Math.Abs(slabBreakdown.Sum(x => x.Charge) - bill.EnergyChargeGross) <= 0.01m;

            return Results.Ok(new
            {
                bill.Id,
                Consumer = new { consumer.AccountNumber, consumer.Name },
                Tariff = new { tariff.Id, tariff.Name, tariff.Category, tariff.FixedChargePerUnitPerMonth, tariff.PrepaidEnergyRebatePercent, tariff.Status },
                Reading = new { reading.ConsumptionKwh, reading.PeriodStart, reading.PeriodEnd },
                SlabBreakdown = slabBreakdown,
                SlabBreakdownReconciles = slabBreakdownReconciles,
                bill.EnergyChargeGross,
                bill.PrepaidRebateAmount,
                EnergyChargeNet = bill.EnergyChargeGross - bill.PrepaidRebateAmount,
                bill.FixedCharge,
                bill.ElectricityDutyAmount,
                bill.FppasAmount,
                bill.FppasChargeId,
                bill.TmcAmount,
                bill.CpmcAmount,
                bill.ArrearsAmount,
                bill.ArrearsRecovered,
                bill.Amount,
                bill.AmountPaid,
                bill.Status,
                bill.GeneratedAt,
            });
        })
        .WithName("GetBillById")
        .RequireAuthorization();


        // Two-stage daily DLP billing, dispatched automatically by BillingProcessingWorker within its
        // two windows (8:30-9:30 AM / 12:30-1:30 PM); exposed here too so the demo can trigger either
        // stage manually without waiting for the clock. `cutoff` lets a manual/demo call specify exactly
        // which receipt-time boundary to bill against (defaults to 8:00 AM / 12:00 PM of `billingDate`'s
        // following day, matching the worker's own real cutoffs) — see IBillingEngineService's doc
        // comment for the full stage-1-vs-stage-2 rule.
        app.MapPost("/api/v1/billing/daily/{billingDate}/stage1", async (DateOnly billingDate, DateTime? cutoff, IBillingEngineService billingEngine, PrepaidEngineDbContext db, ClaimsPrincipal user) =>
        {
            var stage1Cutoff = cutoff ?? billingDate.AddDays(1).ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
            var results = await billingEngine.ProcessDailyStage1Async(billingDate, stage1Cutoff);
            Audit(db, "BillingRun", billingDate.ToString("yyyy-MM-dd"), "BILLING_STAGE1_TRIGGERED", user.Identity?.Name ?? "unknown",
                details: $"Manual trigger for {billingDate:yyyy-MM-dd}; {results.Count(r => !r.Skipped)} consumers processed, {results.Count(r => r.Skipped)} skipped.");
            await db.SaveChangesAsync();
            return Results.Ok(results);
        })
        .WithName("ProcessDailyBillingStage1")
        .RequireAuthorization("DataAdmin");


        app.MapPost("/api/v1/billing/daily/{billingDate}/stage2", async (DateOnly billingDate, DateTime? cutoff, IBillingEngineService billingEngine, PrepaidEngineDbContext db, ClaimsPrincipal user) =>
        {
            var stage2Cutoff = cutoff ?? billingDate.AddDays(1).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
            var results = await billingEngine.ProcessDailyStage2Async(billingDate, stage2Cutoff);
            Audit(db, "BillingRun", billingDate.ToString("yyyy-MM-dd"), "BILLING_STAGE2_TRIGGERED", user.Identity?.Name ?? "unknown",
                details: $"Manual trigger for {billingDate:yyyy-MM-dd}; {results.Count(r => !r.Skipped)} consumers processed, {results.Count(r => r.Skipped)} skipped.");
            await db.SaveChangesAsync();
            return Results.Ok(results);
        })
        .WithName("ProcessDailyBillingStage2")
        .RequireAuthorization("DataAdmin");
    }
}
