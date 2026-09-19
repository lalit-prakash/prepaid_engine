using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Dashboard;

/// <summary>
/// GET /api/v1/dashboard/summary: everything the dashboard's tiles and panels need, computed by the database
/// as counts, sums and a few top-N rows. It replaces the dashboard downloading whole consumer, exception,
/// hold, notification and command lists, which does not survive a million consumers. Nothing here is
/// invented: every number is a count or sum over stored rows.
/// </summary>
public static class DashboardEndpoints
{
    private const int AttentionRows = 10;
    private const int RecentRows = 4;

    private sealed record AttentionItem(string Id, string Severity, string Title, string Detail, DateTime At, string Link);

    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/dashboard/summary", async (PrepaidEngineDbContext db, Microsoft.Extensions.Options.IOptions<PrepaidEngine.Application.Wallets.LowBalanceOptions> lowBalance) =>
        {
            decimal? threshold = lowBalance.Value.ThresholdRs; // null: below each wallet's own emergency credit limit
            // Consumers and wallets: one grouped query, no rows returned.
            var consumers = await db.Consumers.AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Active = g.Count(c => c.ConnectionStatus == ConnectionStatus.Active),
                    Disconnected = g.Count(c => c.ConnectionStatus == ConnectionStatus.Disconnected),
                    LowBalance = g.Count(c => c.Wallet.Balance < (threshold ?? c.Wallet.EmergencyCreditLimit)),
                    LowBalanceConnected = g.Count(c => c.ConnectionStatus != ConnectionStatus.Disconnected && c.Wallet.Balance < (threshold ?? c.Wallet.EmergencyCreditLimit)),
                    WalletTotal = g.Sum(c => (decimal?)c.Wallet.Balance) ?? 0m,
                })
                .FirstOrDefaultAsync();

            // Billing progress for the most recent daily load profile date.
            object? billing = null;
            var latestDate = await db.DailyLoadProfiles.AsNoTracking().MaxAsync(d => (DateOnly?)d.ProfileDate);
            if (latestDate is { } date)
            {
                var day = await db.DailyLoadProfiles.AsNoTracking()
                    .Where(d => d.ProfileDate == date)
                    .GroupBy(_ => 1)
                    .Select(g => new
                    {
                        Total = g.Count(),
                        Successful = g.Count(d => d.Status == DailyProfileStatus.Billed || d.Status == DailyProfileStatus.Reconciled),
                        Failed = g.Count(d => d.Status == DailyProfileStatus.Rejected),
                        LatestReceivedAt = g.Max(d => d.ReceivedAt),
                    })
                    .FirstAsync();
                billing = new { ProfileDate = date.ToString("yyyy-MM-dd"), day.Total, day.Successful, day.Failed, Pending = day.Total - day.Successful - day.Failed, day.LatestReceivedAt };
            }

            // Attention: counts over everything, plus the newest few of each kind.
            var items = new List<AttentionItem>();
            int critical = 0, warning = 0;

            var openExceptions = db.OperationalExceptions.AsNoTracking().Where(e => e.Status == OperationalExceptionStatus.Open);
            critical += await openExceptions.CountAsync();
            items.AddRange((await (from e in openExceptions join c in db.Consumers.AsNoTracking() on e.ConsumerId equals c.Id
                                   orderby e.CreatedAt descending
                                   select new { e.Id, e.Description, c.Name, c.AccountNumber, e.CreatedAt }).Take(AttentionRows).ToListAsync())
                .Select(e => new AttentionItem($"exc-{e.Id}", "critical", e.Description, $"{e.Name} ({e.AccountNumber})", e.CreatedAt, "/exceptions")));

            var activeHolds = db.MeterBillingControls.AsNoTracking().Where(h => h.ActualBillingBlocked);
            warning += await activeHolds.CountAsync();
            items.AddRange((await (from h in activeHolds join c in db.Consumers.AsNoTracking() on h.ConsumerId equals c.Id
                                   join m in db.Meters.AsNoTracking() on h.MeterId equals m.Id
                                   orderby h.BlockedAt descending
                                   select new { h.Id, h.BlockReason, c.Name, c.AccountNumber, m.MeterNumber, h.BlockedAt }).Take(AttentionRows).ToListAsync())
                .Select(h => new AttentionItem($"hold-{h.Id}", "warning", h.BlockReason, $"{h.Name} ({h.AccountNumber}) - Meter {h.MeterNumber}", h.BlockedAt, "/billing-holds")));

            var unsent = db.NotificationEvents.AsNoTracking().Where(n => n.Status != NotificationStatus.Sent);
            critical += await unsent.CountAsync(n => n.Status == NotificationStatus.Failed);
            warning += await unsent.CountAsync(n => n.Status != NotificationStatus.Failed);
            items.AddRange((await (from n in unsent join c in db.Consumers.AsNoTracking() on n.ConsumerId equals c.Id
                                   orderby n.CreatedAt descending
                                   select new { n.Id, n.Message, n.Status, c.Name, c.AccountNumber, n.CreatedAt }).Take(AttentionRows).ToListAsync())
                .Select(n => new AttentionItem($"notif-{n.Id}", n.Status == NotificationStatus.Failed ? "critical" : "warning", n.Message, $"{n.Name} ({n.AccountNumber})", n.CreatedAt, "/notifications")));

            // Payment received but the meter never confirmed the credit: always critical.
            var creditFailures =
                from r in db.RechargeTransactions.AsNoTracking()
                join mc in db.MeterCommands.AsNoTracking() on r.Id equals mc.RechargeTransactionId
                join c in db.Consumers.AsNoTracking() on r.ConsumerId equals c.Id
                where mc.Status == MeterCommandStatus.Failed || mc.Status == MeterCommandStatus.TimedOut
                select new { r.Id, r.Amount, mc.Status, c.Name, c.AccountNumber, At = r.CompletedAt ?? r.InitiatedAt };
            critical += await creditFailures.CountAsync();
            items.AddRange((await creditFailures.OrderByDescending(x => x.At).Take(AttentionRows).ToListAsync())
                .Select(x => new AttentionItem($"credit-{x.Id}", "critical",
                    $"Meter credit {(x.Status == MeterCommandStatus.Failed ? "failed" : "timed out")} for ₹{x.Amount:0.##}",
                    $"{x.Name} ({x.AccountNumber}) - payment received, meter not credited", x.At, $"/recharge/{x.Id}")));

            var pendingTariffs = db.TariffChangeRequests.AsNoTracking()
                .Where(t => t.Status == TariffChangeRequestStatus.PendingApproval || t.Status == TariffChangeRequestStatus.Scheduled);
            warning += await pendingTariffs.CountAsync();
            items.AddRange((await pendingTariffs.OrderByDescending(t => t.CreatedAt).Take(AttentionRows)
                    .Select(t => new { t.Id, t.ProposedName, t.Status, t.SubmittedBy, t.CreatedBy, t.SubmittedAt, t.ApprovedAt, t.CommencementDate, t.CreatedAt })
                    .ToListAsync())
                .Select(t => t.Status == TariffChangeRequestStatus.PendingApproval
                    ? new AttentionItem($"tariff-{t.Id}", "warning", $"Tariff approval pending: {t.ProposedName}", $"Submitted by {t.SubmittedBy ?? t.CreatedBy}", t.SubmittedAt ?? t.CreatedAt, $"/tariffs/change-requests/{t.Id}")
                    : new AttentionItem($"tariff-{t.Id}", "warning", $"Tariff activation scheduled: {t.ProposedName}",
                        $"Commences {(t.CommencementDate is { } d ? d.ToString("yyyy-MM-dd") : "on a date not recorded")}", t.ApprovedAt ?? t.CreatedAt, $"/tariffs/change-requests/{t.Id}")));

            var recentConnectivity = await (
                from cmd in db.ConnectivityCommands.AsNoTracking()
                join c in db.Consumers.AsNoTracking() on cmd.ConsumerId equals c.Id
                orderby cmd.CreatedAt descending
                select new { cmd.Id, c.Name, c.AccountNumber, MeterNumber = c.Meter.MeterNumber, cmd.CommandType, cmd.Status, cmd.CreatedAt })
                .Take(RecentRows).ToListAsync();

            return Results.Ok(new
            {
                Consumers = consumers ?? new { Total = 0, Active = 0, Disconnected = 0, LowBalance = 0, LowBalanceConnected = 0, WalletTotal = 0m },
                Billing = billing,
                Attention = new { Critical = critical, Warning = warning, Items = items.OrderByDescending(i => i.At).Take(AttentionRows) },
                RecentConnectivity = recentConnectivity,
            });
        })
        .WithName("DashboardSummary")
        .RequireAuthorization();
    }
}
