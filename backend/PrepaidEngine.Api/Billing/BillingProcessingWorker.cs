using PrepaidEngine.Application.Billing;

namespace PrepaidEngine.Api.Billing;

/// <summary>
/// A local/demo background worker for the two-stage DLP billing pipeline. Every minute, checks
/// whether it's within the Stage 1 dispatch window (8:30-9:30 AM) or the Stage 2 dispatch window
/// (12:30-1:30 PM); if so, and that stage hasn't already run today, processes the previous
/// calendar day's DLP charge for that stage — see <see cref="IBillingEngineService"/>'s doc
/// comment for the full rule.
///
/// Intentionally simple, per the spec's own framing: production should replace this with a
/// durable scheduler/job framework (with retry guarantees) once operational scale requires it —
/// this is not that, and is not meant to be mistaken for it.
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
    private DateOnly? _lastStage1Processed;
    private DateOnly? _lastStage2Processed;

    public BillingProcessingWorker(IServiceScopeFactory scopeFactory, ILogger<BillingProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Billing processing worker tick failed.");
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
            await billingEngine.ProcessDailyStage1Async(billingDate, stage1CutoffUtc, cancellationToken);
            _lastStage1Processed = today;
            _logger.LogInformation("Processed Stage 1 DLP charge for {BillingDate}.", billingDate);
        }

        if (timeOfDay >= Stage2WindowStart && timeOfDay <= Stage2WindowEnd && _lastStage2Processed != today)
        {
            using var scope = _scopeFactory.CreateScope();
            var billingEngine = scope.ServiceProvider.GetRequiredService<IBillingEngineService>();
            var stage2CutoffUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Add(Stage2Cutoff);
            await billingEngine.ProcessDailyStage2Async(billingDate, stage2CutoffUtc, cancellationToken);
            _lastStage2Processed = today;
            _logger.LogInformation("Processed Stage 2 DLP charge (+ provisional) for {BillingDate}.", billingDate);
        }
    }
}
