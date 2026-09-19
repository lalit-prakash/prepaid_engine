using PrepaidEngine.Infrastructure.MeterCommands;

namespace PrepaidEngine.Api.MeterCommands;

/// <summary>Bound from the "MeterCommandWorker" configuration section.</summary>
public sealed class MeterCommandWorkerOptions
{
    public const string SectionName = "MeterCommandWorker";

    /// <summary>How often the worker looks for queued commands when there is nothing to send.</summary>
    public int PollSeconds { get; set; } = 2;

    /// <summary>Commands sent per round before looking again.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>A command sent this long ago with no outcome is marked timed out.</summary>
    public int StuckAfterMinutes { get; set; } = 10;
}

/// <summary>
/// Background sender for meter credit commands (see <see cref="MeterCommandDispatcher"/>). Recharge requests only
/// queue a command; this worker delivers it, so a slow or unavailable meter/MDM layer never holds up a recharge.
/// Safe to run on several API instances at once.
/// </summary>
public class MeterCommandWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MeterCommandWorkerOptions _options;
    private readonly ILogger<MeterCommandWorker> _logger;
    private DateTime _lastRecovery = DateTime.MinValue;

    public MeterCommandWorker(IServiceScopeFactory scopeFactory, Microsoft.Extensions.Options.IOptions<MeterCommandWorkerOptions> options, ILogger<MeterCommandWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<MeterCommandDispatcher>();
                sent = await dispatcher.DispatchPendingAsync(_options.BatchSize, stoppingToken);

                if (DateTime.UtcNow - _lastRecovery > TimeSpan.FromMinutes(1))
                {
                    _lastRecovery = DateTime.UtcNow;
                    var recovered = await dispatcher.RecoverStuckAsync(TimeSpan.FromMinutes(_options.StuckAfterMinutes), stoppingToken);
                    if (recovered > 0) _logger.LogWarning("Timed out {Count} meter command(s) that were sent but never got an outcome.", recovered);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meter command worker round failed.");
            }

            // Keep going straight away while there is a backlog; otherwise wait.
            if (sent < _options.BatchSize)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(_options.PollSeconds), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
