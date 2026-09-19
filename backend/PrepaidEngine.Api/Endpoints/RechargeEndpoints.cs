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

/// <summary>Recharge endpoints, moved out of Program.cs unchanged.</summary>
public static class RechargeEndpoints
{
    public static void MapRechargeEndpoints(this WebApplication app)
    {
        // Recharge Operations read endpoints — real RechargeTransaction records across all consumers.
        // Note: RechargeStatus has no explicit "Pending" value — the RMS-Pending branch of the POST
        // endpoint below deliberately leaves a transaction in its initial "Initiated" state (RMS
        // hasn't told us Success or Failed yet), so "Initiated" here doubles as "Pending" in the UI.
        app.MapGet("/api/v1/recharges", async (HttpContext http, string? accountNumber, PrepaidEngineDbContext db) =>
        {
            // Left join to MeterCommands: a recharge that never reached RMS Success (or hasn't been
            // dispatched to the meter yet) legitimately has no command row — that must render as "no
            // meter command exists", not be dropped from the list or crash the query.
            var recharges = await (
                from r in db.RechargeTransactions
                join c in db.Consumers on r.ConsumerId equals c.Id
                join mcOuter in db.MeterCommands on r.Id equals mcOuter.RechargeTransactionId into mcGroup
                from mc in mcGroup.DefaultIfEmpty()
                where accountNumber == null || c.AccountNumber == accountNumber
                orderby r.InitiatedAt descending
                select new
                {
                    r.Id,
                    c.AccountNumber,
                    c.Name,
                    r.Amount,
                    r.RmsReferenceId,
                    r.Status,
                    r.InitiatedAt,
                    r.CompletedAt,
                    MeterCommandStatus = mc == null ? (MeterCommandStatus?)null : mc.Status,
                })
                .Take(accountNumber == null ? int.MaxValue : 50)
                .ToCappedListAsync(http);

            return Results.Ok(recharges);
        })
        .WithName("ListRecharges")
        .RequireAuthorization();


        // Server-side searchable, keyset-paginated recharge list for Recharge Operations (the unpaginated
        // ListRecharges above remains for per-consumer views). Ordered newest first; the cursor is
        // "<InitiatedAt ticks>_<Id>" so ties on timestamp still page deterministically. Payment status and
        // meter-credit status are separate filters on purpose: RMS confirming payment never implies the
        // meter was credited. meterCredit=None matches recharges with no meter command at all.
        app.MapGet("/api/v1/recharges/search", async (
            string? q, RechargeStatus? paymentStatus, string? meterCredit, string? after, int? pageSize,
            PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);

            var query =
                from r in db.RechargeTransactions.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on r.ConsumerId equals c.Id
                join mcOuter in db.MeterCommands.AsNoTracking() on r.Id equals mcOuter.RechargeTransactionId into mcGroup
                from mc in mcGroup.DefaultIfEmpty()
                select new { r, c, mc };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                var prefix = term + "%";
                var contains = "%" + term + "%";
                query = query.Where(x =>
                    EF.Functions.ILike(x.c.AccountNumber, prefix, "\\") ||
                    EF.Functions.ILike(x.r.RmsReferenceId, prefix, "\\") ||
                    EF.Functions.ILike(x.c.Name, contains, "\\"));
            }
            if (paymentStatus.HasValue)
                query = query.Where(x => x.r.Status == paymentStatus.Value);
            if (!string.IsNullOrWhiteSpace(meterCredit))
            {
                if (string.Equals(meterCredit, "None", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(x => x.mc == null);
                else if (string.Equals(meterCredit, "FailedOrTimedOut", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(x => x.mc != null && (x.mc.Status == MeterCommandStatus.Failed || x.mc.Status == MeterCommandStatus.TimedOut));
                else if (Enum.TryParse<MeterCommandStatus>(meterCredit, true, out var mcs))
                    query = query.Where(x => x.mc != null && x.mc.Status == mcs);
                else
                    return Results.BadRequest(new { error = $"Unknown meterCredit value '{meterCredit}'." });
            }

            var totalCount = await query.CountAsync();

            if (!string.IsNullOrEmpty(after))
            {
                var parts = after.Split('_', 2);
                if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var afterId))
                    return Results.BadRequest(new { error = "Invalid cursor." });
                var afterAt = new DateTime(ticks, DateTimeKind.Utc);
                query = query.Where(x => x.r.InitiatedAt < afterAt || (x.r.InitiatedAt == afterAt && x.r.Id.CompareTo(afterId) < 0));
            }

            var rows = await query
                .OrderByDescending(x => x.r.InitiatedAt).ThenByDescending(x => x.r.Id)
                .Take(size + 1)
                .Select(x => new
                {
                    x.r.Id,
                    x.c.AccountNumber,
                    x.c.Name,
                    MeterNumber = x.c.Meter.MeterNumber,
                    x.r.Amount,
                    x.r.RmsReferenceId,
                    x.r.Status,
                    x.r.InitiatedAt,
                    x.r.CompletedAt,
                    MeterCommandStatus = x.mc == null ? (MeterCommandStatus?)null : x.mc.Status,
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            var nextCursor = hasMore ? $"{items[^1].InitiatedAt.Ticks}_{items[^1].Id}" : null;
            return Results.Ok(new { items, nextCursor, totalCount });
        })
        .WithName("SearchRecharges")
        .RequireAuthorization();


        // Aggregate counts for the Recharge Operations KPI strip, computed in the database so the
        // browser never has to load every recharge to render a KPI.
        app.MapGet("/api/v1/recharges/summary", async (PrepaidEngineDbContext db) =>
        {
            var byStatus = await db.RechargeTransactions.AsNoTracking()
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(r => r.Amount) })
                .ToListAsync();
            var byCredit = await db.MeterCommands.AsNoTracking()
                .GroupBy(m => m.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            int Payments(RechargeStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
            int Credits(MeterCommandStatus s) => byCredit.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

            return Results.Ok(new
            {
                Total = byStatus.Sum(x => x.Count),
                PaymentSuccess = Payments(RechargeStatus.Success),
                PaymentFailed = Payments(RechargeStatus.Failed),
                PaymentPending = Payments(RechargeStatus.Initiated),
                PaymentReversed = Payments(RechargeStatus.Reversed),
                AmountSucceeded = byStatus.FirstOrDefault(x => x.Status == RechargeStatus.Success)?.Amount ?? 0m,
                MeterCredited = Credits(MeterCommandStatus.Acknowledged),
                MeterCreditAwaiting = Credits(MeterCommandStatus.Queued) + Credits(MeterCommandStatus.Sent),
                MeterCreditFailed = Credits(MeterCommandStatus.Failed) + Credits(MeterCommandStatus.TimedOut),
            });
        })
        .WithName("GetRechargeSummary")
        .RequireAuthorization();


        app.MapGet("/api/v1/recharges/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
        {
            var recharge = await db.RechargeTransactions.FirstOrDefaultAsync(r => r.Id == id);
            if (recharge is null)
                return Results.NotFound();

            var consumer = await db.Consumers.Include(c => c.Wallet).Include(c => c.Meter).FirstOrDefaultAsync(c => c.Id == recharge.ConsumerId);
            if (consumer is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Recharge references missing data",
                    detail: $"Recharge {id} references a consumer that no longer exists.");
            }

            var meterCommand = await db.MeterCommands.FirstOrDefaultAsync(m => m.RechargeTransactionId == id);

            return Results.Ok(new
            {
                recharge.Id,
                Consumer = new { consumer.AccountNumber, consumer.Name, consumer.Meter.MeterNumber },
                recharge.Amount,
                recharge.RmsReferenceId,
                recharge.Status,
                recharge.InitiatedAt,
                recharge.CompletedAt,
                WalletBalance = consumer.Wallet.Balance,
                MeterCommand = meterCommand is null ? null : new
                {
                    meterCommand.Id,
                    meterCommand.Status,
                    meterCommand.RetryCount,
                    meterCommand.ErrorMessage,
                    meterCommand.ExternalCommandId,
                    meterCommand.ResponseCode,
                    meterCommand.ResponseMessage,
                    meterCommand.CreatedAt,
                    meterCommand.SentAt,
                    meterCommand.AcknowledgedAt,
                },
            });
        })
        .WithName("GetRechargeById")
        .RequireAuthorization();


        // Meter Credit read endpoints — real MeterCommand records across all consumers, each traceable
        // back to the RechargeTransaction that triggered it (see MeterCommand's doc comment for why
        // these are separate entities with separate lifecycles).
        app.MapGet("/api/v1/meter-commands", async (HttpContext http, PrepaidEngineDbContext db) =>
        {
            var commands = await (
                from m in db.MeterCommands
                join c in db.Consumers on m.ConsumerId equals c.Id
                join r in db.RechargeTransactions on m.RechargeTransactionId equals r.Id
                orderby m.CreatedAt descending
                select new
                {
                    m.Id,
                    c.AccountNumber,
                    c.Name,
                    m.CreditAmount,
                    m.Status,
                    m.RetryCount,
                    m.ErrorMessage,
                    m.CreatedAt,
                    m.SentAt,
                    m.AcknowledgedAt,
                    RechargeTransactionId = r.Id,
                    r.RmsReferenceId,
                })
                .ToCappedListAsync(http);

            return Results.Ok(commands);
        })
        .WithName("ListMeterCommands")
        .RequireAuthorization();


        app.MapGet("/api/v1/meter-commands/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
        {
            var command = await db.MeterCommands.FirstOrDefaultAsync(m => m.Id == id);
            if (command is null)
                return Results.NotFound();

            var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == command.ConsumerId);
            var recharge = await db.RechargeTransactions.FirstOrDefaultAsync(r => r.Id == command.RechargeTransactionId);
            if (consumer is null || recharge is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Meter command references missing data",
                    detail: $"Meter command {id} references a consumer or recharge that no longer exists.");
            }

            return Results.Ok(new
            {
                command.Id,
                Consumer = new { consumer.AccountNumber, consumer.Name },
                command.CreditAmount,
                command.Status,
                command.RetryCount,
                command.ErrorMessage,
                command.ExternalCommandId,
                command.ResponseCode,
                command.ResponseMessage,
                command.CreatedAt,
                command.SentAt,
                command.AcknowledgedAt,
                Recharge = new { recharge.Id, recharge.RmsReferenceId, recharge.Amount },
            });
        })
        .WithName("GetMeterCommandById")
        .RequireAuthorization();


        // Retries a Failed/TimedOut meter command: resets it to Queued via MeterCommand.Retry()
        // (incrementing RetryCount, clearing the prior error/SentAt), then dispatches it again through
        // IMeterCommandClient exactly like the original attempt — never fabricates a retry result.
        app.MapPost("/api/v1/meter-commands/{id:guid}/retry", async (
            Guid id,
            PrepaidEngineDbContext db,
            ClaimsPrincipal user) =>
        {
            var command = await db.MeterCommands.FirstOrDefaultAsync(m => m.Id == id);
            if (command is null)
                return Results.NotFound();

            try
            {
                command.Retry();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            // Back to Queued: the MeterCommandWorker sends it within a couple of seconds and records the outcome.
            Audit(db, nameof(MeterCommand), command.Id.ToString(), "METER_COMMAND_RETRY_QUEUED", user.Identity?.Name ?? "unknown",
                details: $"Retry #{command.RetryCount}.");
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                command.Id,
                command.Status,
                command.RetryCount,
                command.ErrorMessage,
            });
        })
        .WithName("RetryMeterCommand")
        .RequireAuthorization("Operations");
    }
}
