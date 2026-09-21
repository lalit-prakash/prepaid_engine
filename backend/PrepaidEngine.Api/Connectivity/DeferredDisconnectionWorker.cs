using PrepaidEngine.Api.Health;
using PrepaidEngine.Infrastructure.Connectivity;

namespace PrepaidEngine.Api.Connectivity;

/// <summary>
/// Every five minutes, once the disconnection window (11 AM to 4 PM IST) is open, disconnects consumers whose credit ran out during the credit hours
/// (see <see cref="DeferredDisconnectionService"/>). Safe on several API instances: a consumer already disconnected by another is no longer Active.
/// </summary>
public sealed class DeferredDisconnectionWorker : BackgroundService
{
    private const string StatusName = "DeferredDisconnectionWorker";
    private const int EverySeconds = 300;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeferredDisconnectionWorker> _logger;
    private readonly WorkerStatusRegistry _status;

    public DeferredDisconnectionWorker(IServiceScopeFactory scopeFactory, ILogger<DeferredDisconnectionWorker> logger, WorkerStatusRegistry status)
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
                var handled = await scope.ServiceProvider.GetRequiredService<DeferredDisconnectionService>().RunAsync(DateTime.UtcNow, stoppingToken);
                if (handled > 0) _logger.LogInformation("Disconnection window open: {Count} consumer(s) beyond their emergency credit were disconnected.", handled);
                _status.Success(StatusName, EverySeconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The deferred disconnection run failed.");
                _status.Failure(StatusName, EverySeconds, ex);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(EverySeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
