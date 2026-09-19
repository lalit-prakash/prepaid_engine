using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Application.Conversion;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Health;

/// <summary>
/// GET /api/v1/system/health and /api/v1/system/integrations: what is actually running, measured, not assumed.
/// Health times a real database round trip, reports each worker's last success or failure, and counts the queues
/// that would back up if something stopped. Integrations lists each adapter with the class behind it and what the
/// database shows it has done recently. An adapter that is a simulator says so.
/// </summary>
public static class SystemEndpoints
{
    private static readonly DateTime StartedAt = DateTime.UtcNow;

    public static void MapSystemEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/system/health", async (PrepaidEngineDbContext db, WorkerStatusRegistry workers, IWebHostEnvironment env) =>
        {
            var now = DateTime.UtcNow;

            // Database: one real round trip, timed.
            string dbState; double? dbMs = null; int applied = 0, pending = 0; string? dbError = null;
            try
            {
                var sw = Stopwatch.StartNew();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                sw.Stop();
                dbMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
                applied = (await db.Database.GetAppliedMigrationsAsync()).Count();
                pending = (await db.Database.GetPendingMigrationsAsync()).Count();
                dbState = pending > 0 ? "Degraded" : "Healthy";
            }
            catch (Exception ex)
            {
                dbState = "Down";
                dbError = ex.GetType().Name;
            }

            var workerRows = workers.Snapshot().Select(w => new
            {
                w.Name, w.ExpectedEverySeconds, w.LastSuccessAt, w.LastFailureAt, w.LastError, w.Runs, w.Failures,
                State = WorkerStatusRegistry.StateOf(w, now),
            }).ToList();

            object? queues = null; object? recentRuns = null;
            if (dbState != "Down")
            {
                var queued = db.MeterCommands.AsNoTracking().Where(m => m.Status == MeterCommandStatus.Queued);
                var oldestQueued = await queued.MinAsync(m => (DateTime?)m.CreatedAt);
                queues = new
                {
                    MeterCommandsQueued = await queued.CountAsync(),
                    OldestQueuedSeconds = oldestQueued is null ? (double?)null : Math.Round((now - oldestQueued.Value).TotalSeconds),
                    MeterCommandsInFlight = await db.MeterCommands.AsNoTracking().CountAsync(m => m.Status == MeterCommandStatus.Sent),
                    ConnectivityCommandsPending = await db.ConnectivityCommands.AsNoTracking().CountAsync(c => c.Status == ConnectivityCommandStatus.Queued || c.Status == ConnectivityCommandStatus.Sent),
                    NotificationsPending = await db.NotificationEvents.AsNoTracking().CountAsync(n => n.Status == NotificationStatus.Pending),
                    OpenExceptions = await db.OperationalExceptions.AsNoTracking().CountAsync(e => e.Status == OperationalExceptionStatus.Open),
                    ActiveBillingHolds = await db.MeterBillingControls.AsNoTracking().CountAsync(h => h.ActualBillingBlocked),
                };
                recentRuns = await db.BillingRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(5)
                    .Select(r => new { r.RunType, r.BillingDate, r.Status, r.ConsumerCount, r.ExceptionCount, r.StartedAt, r.CompletedAt, r.LastHeartbeatAt })
                    .ToListAsync();
            }

            var oldest = (queues as dynamic)?.OldestQueuedSeconds as double?;
            var overall = dbState == "Down" ? "Down"
                : dbState == "Degraded" || workerRows.Any(w => w.State is "Stale" or "Failing") || oldest > 300 ? "Degraded"
                : "Healthy";

            return Results.Ok(new
            {
                Overall = overall,
                CheckedAt = now,
                Api = new
                {
                    State = "Healthy",
                    Environment = env.EnvironmentName,
                    Version = typeof(SystemEndpoints).Assembly.GetName().Version?.ToString() ?? "unknown",
                    StartedAt,
                    UptimeSeconds = (long)(now - StartedAt).TotalSeconds,
                },
                Database = new { State = dbState, LatencyMs = dbMs, AppliedMigrations = applied, PendingMigrations = pending, Error = dbError },
                Workers = workerRows,
                Queues = queues,
                RecentBillingRuns = recentRuns,
            });
        })
        .WithName("SystemHealth")
        .RequireAuthorization();

        app.MapGet("/api/v1/system/integrations", async (
            PrepaidEngineDbContext db, IRmsClient rms, IMeterCommandClient meter, IConnectivityCommandClient connectivity, IPaymentModeChangeClient paymentMode) =>
        {
            var since = DateTime.UtcNow.AddHours(-24);
            static string Mode(object adapter) => adapter.GetType().Name.StartsWith("Mock", StringComparison.Ordinal) ? "Mock" : "Live";

            var outbound = new object[]
            {
                new
                {
                    Name = "RMS (payments)", Purpose = "Confirms a consumer's payment before the wallet is credited",
                    Adapter = rms.GetType().Name, Mode = Mode(rms),
                    LastActivityAt = await db.RechargeTransactions.AsNoTracking().MaxAsync(r => (DateTime?)r.InitiatedAt),
                    Last24hOk = await db.RechargeTransactions.AsNoTracking().CountAsync(r => r.InitiatedAt >= since && r.Status == RechargeStatus.Success),
                    Last24hFailed = await db.RechargeTransactions.AsNoTracking().CountAsync(r => r.InitiatedAt >= since && r.Status == RechargeStatus.Failed),
                },
                new
                {
                    Name = "Meter credit (MDM/HES)", Purpose = "Sends the credit to the smart meter after a recharge",
                    Adapter = meter.GetType().Name, Mode = Mode(meter),
                    LastActivityAt = await db.MeterCommands.AsNoTracking().MaxAsync(m => (DateTime?)m.SentAt),
                    Last24hOk = await db.MeterCommands.AsNoTracking().CountAsync(m => m.CreatedAt >= since && m.Status == MeterCommandStatus.Acknowledged),
                    Last24hFailed = await db.MeterCommands.AsNoTracking().CountAsync(m => m.CreatedAt >= since && (m.Status == MeterCommandStatus.Failed || m.Status == MeterCommandStatus.TimedOut)),
                },
                new
                {
                    Name = "Remote disconnect / reconnect (HES)", Purpose = "Sends RC/DC commands to the meter",
                    Adapter = connectivity.GetType().Name, Mode = Mode(connectivity),
                    LastActivityAt = await db.ConnectivityCommands.AsNoTracking().MaxAsync(c => (DateTime?)c.SentAt),
                    Last24hOk = await db.ConnectivityCommands.AsNoTracking().CountAsync(c => c.CreatedAt >= since && c.Status == ConnectivityCommandStatus.Acknowledged),
                    Last24hFailed = await db.ConnectivityCommands.AsNoTracking().CountAsync(c => c.CreatedAt >= since && (c.Status == ConnectivityCommandStatus.Failed || c.Status == ConnectivityCommandStatus.TimedOut)),
                },
                new
                {
                    Name = "Payment mode change (MDMS to HES)", Purpose = "Switches a converted meter from postpaid to prepaid",
                    Adapter = paymentMode.GetType().Name, Mode = Mode(paymentMode),
                    LastActivityAt = await db.PaymentModeChangeCommands.AsNoTracking().MaxAsync(c => (DateTime?)c.SentAt),
                    Last24hOk = await db.PaymentModeChangeCommands.AsNoTracking().CountAsync(c => c.CreatedAt >= since && c.Status == PaymentModeChangeStatus.Acknowledged),
                    Last24hFailed = await db.PaymentModeChangeCommands.AsNoTracking().CountAsync(c => c.CreatedAt >= since && (c.Status == PaymentModeChangeStatus.Failed || c.Status == PaymentModeChangeStatus.TimedOut)),
                },
            };

            // Data pushed to this system: nothing here is polled, so the honest signal is when it last arrived.
            var inbound = new object[]
            {
                new { Name = "MDMS: daily load profiles", LatestAt = await db.DailyLoadProfiles.AsNoTracking().MaxAsync(d => (DateTime?)d.ReceivedAt), Last24h = await db.DailyLoadProfiles.AsNoTracking().CountAsync(d => d.ReceivedAt >= since) },
                new { Name = "MDMS: register readings", LatestAt = await db.RegisterReadings.AsNoTracking().MaxAsync(r => (DateTime?)r.ReadingTimestamp), Last24h = await db.RegisterReadings.AsNoTracking().CountAsync(r => r.ReadingTimestamp >= since) },
                new { Name = "MDMS: load survey intervals", LatestAt = await db.LoadSurveyIntervals.AsNoTracking().MaxAsync(l => (DateTime?)l.IntervalStart), Last24h = await db.LoadSurveyIntervals.AsNoTracking().CountAsync(l => l.IntervalStart >= since) },
                new { Name = "MDMS: instantaneous readings", LatestAt = await db.InstantaneousReadings.AsNoTracking().MaxAsync(i => (DateTime?)i.Timestamp), Last24h = await db.InstantaneousReadings.AsNoTracking().CountAsync(i => i.Timestamp >= since) },
                new { Name = "MDMS: meter events", LatestAt = await db.MeterEvents.AsNoTracking().MaxAsync(e => (DateTime?)e.EventTimestamp), Last24h = await db.MeterEvents.AsNoTracking().CountAsync(e => e.EventTimestamp >= since) },
                new { Name = "RMS: conversion requests", LatestAt = await db.ConversionRequests.AsNoTracking().MaxAsync(c => (DateTime?)c.RequestedAt), Last24h = await db.ConversionRequests.AsNoTracking().CountAsync(c => c.RequestedAt >= since) },
                new { Name = "RMS: reconciliation adjustments", LatestAt = await db.ReconciliationAdjustments.AsNoTracking().MaxAsync(r => (DateTime?)r.AppliedAt), Last24h = await db.ReconciliationAdjustments.AsNoTracking().CountAsync(r => r.AppliedAt >= since) },
            };

            return Results.Ok(new { CheckedAt = DateTime.UtcNow, Outbound = outbound, Inbound = inbound });
        })
        .WithName("SystemIntegrations")
        .RequireAuthorization();
    }
}
