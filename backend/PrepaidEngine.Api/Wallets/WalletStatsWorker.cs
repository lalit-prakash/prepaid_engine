using PrepaidEngine.Api.Health;
using PrepaidEngine.Infrastructure.Wallets;

namespace PrepaidEngine.Api.Wallets;

/// <summary>
/// Refreshes today's wallet totals (see DailyWalletStat) once at start-up and then every hour, so the last reading of each
/// day is what the balance-history chart shows. Safe on several API instances (the date is the row's key).
/// </summary>
public sealed class WalletStatsWorker : BackgroundService
{
    private const string StatusName = "WalletStatsWorker";
    private const int EverySeconds = 3600;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WalletStatsWorker> _logger;
    private readonly WorkerStatusRegistry _status;

    public WalletStatsWorker(IServiceScopeFactory scopeFactory, ILogger<WalletStatsWorker> logger, WorkerStatusRegistry status)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _status = status;
        _status.Expect(StatusName, EverySeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<WalletStatsService>();
                var now = DateTime.UtcNow;
                await service.RecordAsync(DateOnly.FromDateTime(now), now, stoppingToken);
                _status.Success(StatusName, EverySeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // for example the database is not reachable yet; try again on the next round
                _logger.LogError(ex, "Recording the daily wallet totals failed.");
                _status.Failure(StatusName, EverySeconds, ex);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(EverySeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
