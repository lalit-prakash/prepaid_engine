using PrepaidEngine.Application.Billing;

namespace PrepaidEngine.Api.Billing;

/// <summary>
/// A local/demo background worker for the two-stage DLP billing pipeline. Every minute, checks
/// whether it's within the Stage 1 dispatch window (8:30-9:30 AM) or the Stage 2 dispatch window
/// (12:30-1:30 PM); if so, and that stage hasn't already run today, processes the previous
/// calendar day's DLP charge for that stage — see <see cref="IBillingEngineService"/>'s doc
/// comment for the full rule.
///
/// Safe to run on several API instances at once: the billing service claims each stage run (one instance
/// wins, the others are told it is running elsewhere), processes consumers in committed batches, and lets
/// another instance take over and resume a run whose owner stopped reporting progress. A stage that is
/// running elsewhere is therefore retried on later ticks rather than marked done here. A dedicated job
/// framework would still add scheduling history and alerting, but is no longer needed for correctness.
/// </summary>
public class BillingProcessingWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Stage1WindowStart = TimeSpan.FromHours(8.5);   // 8:30 AM
    private static readonly TimeSpan Stage1WindowEnd = TimeSpan.FromHours(9.5);     // 9:30 AM
    private static readonly TimeSpan Stage1Cutoff = TimeSpan.FromHours(8);          // 8:00 AM

    private static readonly TimeSpan Stage2WindowStart = TimeSpan.FromHours(12.5);  // 12:30 PM
    private static readonly TimeSpan Stage2WindowEnd = TimeSpan.FromHours(13.5);    // 1:30 PM
    private static readonly TimeSpan Stage2Cutoff = TimeSpan.FromHours(12);         // 12:00 PM

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingProcessingWorker> _logger;
    private readonly PrepaidEngine.Api.Health.WorkerStatusRegistry _status;
    private const string StatusName = "BillingProcessingWorker";
    private DateOnly? _lastStage1Processed;
    private DateOnly? _lastStage2Processed;

    public BillingProcessingWorker(IServiceScopeFactory scopeFactory, ILogger<BillingProcessingWorker> logger, PrepaidEngine.Api.Health.WorkerStatusRegistry status)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _status = status;
        _status.Expect(StatusName, 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
                _status.Success(StatusName, 60);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Billing processing worker tick failed.");
                _status.Failure(StatusName, 60, ex);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var timeOfDay = now.TimeOfDay;
        var today = DateOnly.FromDateTime(now);
        var billingDate = today.AddDays(-1); // DLP bills the previous day's consumption.

        if (timeOfDay >= Stage1WindowStart && timeOfDay <= Stage1WindowEnd && _lastStage1Processed != today)
        {
            using var scope = _scopeFactory.CreateScope();
            var billingEngine = scope.ServiceProvider.GetRequiredService<IBillingEngineService>();
            var stage1CutoffUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Add(Stage1Cutoff);
            var results = await billingEngine.ProcessDailyStage1Async(billingDate, stage1CutoffUtc, cancellationToken);
            if (RunningElsewhere(results))
                _logger.LogInformation("Stage 1 for {BillingDate} is running on another instance; will check again.", billingDate);
            else
            {
                _lastStage1Processed = today;
                _logger.LogInformation("Processed Stage 1 DLP charge for {BillingDate}.", billingDate);
            }
        }

        if (timeOfDay >= Stage2WindowStart && timeOfDay <= Stage2WindowEnd && _lastStage2Processed != today)
        {
            using var scope = _scopeFactory.CreateScope();
            var billingEngine = scope.ServiceProvider.GetRequiredService<IBillingEngineService>();
            var stage2CutoffUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Add(Stage2Cutoff);
            var results = await billingEngine.ProcessDailyStage2Async(billingDate, stage2CutoffUtc, cancellationToken);
            if (RunningElsewhere(results))
                _logger.LogInformation("Stage 2 for {BillingDate} is running on another instance; will check again.", billingDate);
            else
            {
                _lastStage2Processed = today;
                _logger.LogInformation("Processed Stage 2 DLP charge (+ provisional) for {BillingDate}.", billingDate);
            }
        }
    }

    private static bool RunningElsewhere(IReadOnlyList<DailyProcessingResult> results)
        => results.Count == 1 && results[0].Skipped && results[0].SkipReason?.Contains("already running", StringComparison.Ordinal) == true;
}
