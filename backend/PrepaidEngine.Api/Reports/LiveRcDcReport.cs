using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Reports;

/// <summary>
/// Live RC DC Status: for each day, how many disconnects (DC) went out and what came back against them.
///
/// DC columns count Disconnect commands created that day (pending = queued or sent, success = acknowledged; the rest failed
/// or timed out). RC columns count Reconnect commands created that day for a consumer who was disconnected earlier the
/// same day, so a reconnect is always shown against a DC. Recharge columns count that day's recharges by those same
/// consumers made after their DC: MDM success = payment received, pending in MDM = meter credit queued and not yet sent to
/// the head-end, success in HES = meter credit acknowledged, fail in HES = failed or timed out. Eligible DC is a live count
/// (connected consumers whose balance is beyond the emergency credit limit), so it is only given for today: there is no
/// history of it to report for earlier days. Days are UTC calendar days, as in the other reports.
/// </summary>
public static class LiveRcDcReport
{
    private const int MaxDays = 31;

    public static void MapLiveRcDcReport(this WebApplication app)
    {
        app.MapGet("/api/v1/reports/live-rc-dc", async (PrepaidEngineDbContext db, DateTime? from, DateTime? to, [AsParameters] HierarchyQuery h) =>
        {
            var today = DateTime.UtcNow.Date;
            var first = (from ?? today).Date;
            var last = (to ?? first).Date;
            if (last < first) return Results.BadRequest(new { error = "The end date is before the start date." });
            if ((last - first).TotalDays >= MaxDays) return Results.BadRequest(new { error = $"Choose at most {MaxDays} days." });
            var start = DateTime.SpecifyKind(first, DateTimeKind.Utc);
            var end = DateTime.SpecifyKind(last.AddDays(1), DateTimeKind.Utc);

            var consumers = db.Consumers.AsNoTracking().ByHierarchy(h);
            var commands = from cmd in db.ConnectivityCommands.AsNoTracking()
                           join c in consumers on cmd.ConsumerId equals c.Id
                           where cmd.CreatedAt >= start && cmd.CreatedAt < end
                           select cmd;

            var dc = await commands.Where(x => x.CommandType == ConnectivityCommandType.Disconnect)
                .GroupBy(x => x.CreatedAt.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Triggered = g.Count(),
                    Pending = g.Count(x => x.Status == ConnectivityCommandStatus.Queued || x.Status == ConnectivityCommandStatus.Sent),
                    Success = g.Count(x => x.Status == ConnectivityCommandStatus.Acknowledged),
                }).ToListAsync();

            var rc = await commands.Where(r => r.CommandType == ConnectivityCommandType.Reconnect
                    && db.ConnectivityCommands.Any(d => d.ConsumerId == r.ConsumerId && d.CommandType == ConnectivityCommandType.Disconnect
                        && d.CreatedAt.Date == r.CreatedAt.Date && d.CreatedAt < r.CreatedAt))
                .GroupBy(x => x.CreatedAt.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Initiated = g.Count(),
                    Pending = g.Count(x => x.Status == ConnectivityCommandStatus.Queued || x.Status == ConnectivityCommandStatus.Sent),
                    Success = g.Count(x => x.Status == ConnectivityCommandStatus.Acknowledged),
                }).ToListAsync();

            var recharges = await (
                from r in db.RechargeTransactions.AsNoTracking()
                join c in consumers on r.ConsumerId equals c.Id
                where r.InitiatedAt >= start && r.InitiatedAt < end && r.Status == RechargeStatus.Success
                    && db.ConnectivityCommands.Any(d => d.ConsumerId == r.ConsumerId && d.CommandType == ConnectivityCommandType.Disconnect
                        && d.CreatedAt.Date == r.InitiatedAt.Date && d.CreatedAt < r.InitiatedAt)
                join m in db.MeterCommands.AsNoTracking() on r.Id equals m.RechargeTransactionId into meter
                from m in meter.DefaultIfEmpty()
                group m by r.InitiatedAt.Date into g
                select new
                {
                    Date = g.Key,
                    MdmSuccess = g.Count(),
                    PendingInMdm = g.Count(x => x != null && x.Status == MeterCommandStatus.Queued),
                    SuccessInHes = g.Count(x => x != null && x.Status == MeterCommandStatus.Acknowledged),
                    FailInHes = g.Count(x => x != null && (x.Status == MeterCommandStatus.Failed || x.Status == MeterCommandStatus.TimedOut)),
                }).ToListAsync();

            int? eligible = null;
            if (today >= first && today <= last)
                eligible = await consumers.CountAsync(c => c.ConnectionStatus == ConnectionStatus.Active && c.Wallet.Balance < -c.Wallet.EmergencyCreditLimit);

            var rows = Enumerable.Range(0, (int)(last - first).TotalDays + 1).Select(i =>
            {
                var day = first.AddDays(i);
                var d = dc.FirstOrDefault(x => x.Date == day);
                var r = rc.FirstOrDefault(x => x.Date == day);
                var c = recharges.FirstOrDefault(x => x.Date == day);
                return new
                {
                    Date = day,
                    EligibleDc = day == today ? eligible : null,
                    DcTriggered = d?.Triggered ?? 0,
                    DcPending = d?.Pending ?? 0,
                    DcSuccess = d?.Success ?? 0,
                    RcInitiated = r?.Initiated ?? 0,
                    RcPending = r?.Pending ?? 0,
                    RcSuccess = r?.Success ?? 0,
                    RechargeMdmSuccess = c?.MdmSuccess ?? 0,
                    RechargePendingInMdm = c?.PendingInMdm ?? 0,
                    RechargeSuccessInHes = c?.SuccessInHes ?? 0,
                    RechargeFailInHes = c?.FailInHes ?? 0,
                };
            }).OrderByDescending(x => x.Date).ToList();

            return Results.Ok(new { rows, truncated = false, generatedAt = DateTime.UtcNow });
        })
        .WithName("LiveRcDcStatusReport")
        .RequireAuthorization();
    }
}
