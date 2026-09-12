using PrepaidEngine.Application.Billing;

namespace PrepaidEngine.Api.Billing;

/// <summary>
/// A local/demo background worker for the LS/DLP billing pipeline (spec §23). Every minute,
/// checks whether it is within the first ten minutes of a new hour; if so, processes the
/// previous completed hour, and — if the new hour is midnight — also processes the previous
/// day's DLP settlement.
///
/// Intentionally simple, per the spec's own framing: production should replace this with a
/// durable scheduler/job framework (with retry guarantees) once operational scale requires it —
/// this is not that, and is not meant to be mistaken for it.
/// </summary>
public class BillingProcessingWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private const int TriggerWindowMinutes = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingProcessingWorker> _logger;
    private DateTime? _lastHourProcessed;
    private DateOnly? _lastDayProcessed;

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
        if (now.Minute >= TriggerWindowMinutes)
            return;

        var completedHourEnd = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        if (_lastHourProcessed != completedHourEnd)
        {
            using var scope = _scopeFactory.CreateScope();
            var billingEngine = scope.ServiceProvider.GetRequiredService<IBillingEngineService>();
            await billingEngine.ProcessCompletedHourAsync(completedHourEnd, cancellationToken);
            _lastHourProcessed = completedHourEnd;
            _logger.LogInformation("Processed completed hour ending {HourEnd}.", completedHourEnd);
        }

        if (now.Hour == 0)
        {
            var previousDay = DateOnly.FromDateTime(now.AddDays(-1));
            if (_lastDayProcessed != previousDay)
            {
                using var scope = _scopeFactory.CreateScope();
                var billingEngine = scope.ServiceProvider.GetRequiredService<IBillingEngineService>();
                await billingEngine.ProcessDailyAsync(previousDay, cancellationToken);
                _lastDayProcessed = previousDay;
                _logger.LogInformation("Processed daily DLP settlement for {BillingDate}.", previousDay);
            }
        }
    }
}
