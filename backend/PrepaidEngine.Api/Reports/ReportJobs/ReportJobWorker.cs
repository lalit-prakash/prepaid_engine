using Microsoft.Extensions.Options;
using PrepaidEngine.Api.Health;

namespace PrepaidEngine.Api.Reports.ReportJobs;

/// <summary>Background builder for report exports (see <see cref="ReportJobRunner"/>). Safe on several API instances.</summary>
public sealed class ReportJobWorker : BackgroundService
{
    private const string StatusName = "ReportJobWorker";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReportJobOptions _options;
    private readonly ILogger<ReportJobWorker> _logger;
    private readonly WorkerStatusRegistry _status;
    private DateTime _lastHousekeeping = DateTime.MinValue;

    public ReportJobWorker(IServiceScopeFactory scopeFactory, IOptions<ReportJobOptions> options, ILogger<ReportJobWorker> logger, WorkerStatusRegistry status)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _status = status;
        _status.Expect(StatusName, options.Value.PollSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var didWork = false;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ReportJobRunner>();
                didWork = await runner.RunNextAsync(stoppingToken);

                if (DateTime.UtcNow - _lastHousekeeping > TimeSpan.FromMinutes(10))
                {
                    _lastHousekeeping = DateTime.UtcNow;
                    var expired = await runner.ExpireOldAsync(DateTime.UtcNow, stoppingToken);
                    var interrupted = await runner.FailInterruptedAsync(DateTime.UtcNow, TimeSpan.FromHours(2), stoppingToken);
                    if (expired + interrupted > 0) _logger.LogInformation("Report exports: removed {Expired} expired file(s), failed {Interrupted} interrupted job(s).", expired, interrupted);
                }
                _status.Success(StatusName, _options.PollSeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Report job worker round failed.");
                _status.Failure(StatusName, _options.PollSeconds, ex);
            }

            if (!didWork)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(_options.PollSeconds), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
