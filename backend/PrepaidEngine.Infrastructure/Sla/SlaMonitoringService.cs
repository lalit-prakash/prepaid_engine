using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrepaidEngine.Application.Sla;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.Sla;

/// <summary>See <see cref="ISlaMonitoringService"/>.</summary>
public class SlaMonitoringService : ISlaMonitoringService
{
    private readonly PrepaidEngineDbContext _db;
    private readonly SlaMonitoringOptions _options;

    public SlaMonitoringService(PrepaidEngineDbContext db, IOptions<SlaMonitoringOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SlaMetric>> GetSlaSummaryAsync(CancellationToken cancellationToken = default)
    {
        var metrics = new List<SlaMetric>();

        // DLP ingestion latency: GeneratedAt (meter/head-end clock) -> ReceivedAt (this engine's
        // own ingest time). Provisional profiles have no real GeneratedAt to measure against, so
        // they're excluded. The subtraction is done in memory (not via a SQL DATEDIFF) since this
        // project's Npgsql provider has no translation for one.
        var dlpTimestamps = await _db.DailyLoadProfiles
            .Where(d => !d.IsProvisional)
            .Select(d => new { d.GeneratedAt, d.ReceivedAt })
            .ToListAsync(cancellationToken);
        metrics.Add(BuildMetric("DLP Ingestion", _options.DlpIngestionTargetMinutes,
            dlpTimestamps.Select(d => (decimal)(d.ReceivedAt - d.GeneratedAt).TotalMinutes)));

        // Daily billing-run duration: StartedAt -> CompletedAt, for runs that actually finished
        // (Running has no end yet to measure).
        var billingRunTimestamps = await _db.BillingRuns
            .Where(b => b.CompletedAt != null)
            .Select(b => new { b.StartedAt, CompletedAt = b.CompletedAt!.Value })
            .ToListAsync(cancellationToken);
        metrics.Add(BuildMetric("Billing Run Duration", _options.BillingRunTargetMinutes,
            billingRunTimestamps.Select(b => (decimal)(b.CompletedAt - b.StartedAt).TotalMinutes)));

        // Recharge completion: InitiatedAt -> CompletedAt, for recharges that actually completed
        // (Success or Failed both count — the SLA is "how long until we knew", not "how long
        // until it succeeded").
        var rechargeTimestamps = await _db.RechargeTransactions
            .Where(r => r.CompletedAt != null)
            .Select(r => new { r.InitiatedAt, CompletedAt = r.CompletedAt!.Value })
            .ToListAsync(cancellationToken);
        metrics.Add(BuildMetric("Recharge Completion", _options.RechargeCompletionTargetMinutes,
            rechargeTimestamps.Select(r => (decimal)(r.CompletedAt - r.InitiatedAt).TotalMinutes)));

        // Meter credit acknowledgement: CreatedAt -> AcknowledgedAt (Acknowledged only — a
        // Failed/TimedOut command never reached the state this SLA measures).
        var meterCreditTimestamps = await _db.MeterCommands
            .Where(m => m.Status == MeterCommandStatus.Acknowledged && m.AcknowledgedAt != null)
            .Select(m => new { m.CreatedAt, AcknowledgedAt = m.AcknowledgedAt!.Value })
            .ToListAsync(cancellationToken);
        metrics.Add(BuildMetric("Meter Credit Acknowledgement", _options.MeterCreditTargetMinutes,
            meterCreditTimestamps.Select(m => (decimal)(m.AcknowledgedAt - m.CreatedAt).TotalMinutes)));

        // RC/DC command acknowledgement: CreatedAt -> AcknowledgedAt.
        var connectivityTimestamps = await _db.ConnectivityCommands
            .Where(c => c.Status == ConnectivityCommandStatus.Acknowledged && c.AcknowledgedAt != null)
            .Select(c => new { c.CreatedAt, AcknowledgedAt = c.AcknowledgedAt!.Value })
            .ToListAsync(cancellationToken);
        metrics.Add(BuildMetric("RC/DC Acknowledgement", _options.ConnectivityCommandTargetMinutes,
            connectivityTimestamps.Select(c => (decimal)(c.AcknowledgedAt - c.CreatedAt).TotalMinutes)));

        return metrics;
    }

    private static SlaMetric BuildMetric(string name, decimal targetMinutes, IEnumerable<decimal> samplesMinutes)
    {
        var samples = samplesMinutes.ToList();
        if (samples.Count == 0)
            return new SlaMetric(name, targetMinutes, null, 0, 0, null, "Unavailable");

        var average = Math.Round(samples.Average(), 2);
        var breaches = samples.Count(s => s > targetMinutes);
        var breachPct = Math.Round((decimal)breaches / samples.Count * 100m, 2);
        var status = breachPct == 0 ? "Met" : breachPct < 20 ? "AtRisk" : "Breached";

        return new SlaMetric(name, targetMinutes, average, samples.Count, breaches, breachPct, status);
    }
}
