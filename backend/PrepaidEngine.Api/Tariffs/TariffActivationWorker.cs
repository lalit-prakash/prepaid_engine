using PrepaidEngine.Infrastructure.Tariffs;

namespace PrepaidEngine.Api.Tariffs;

/// <summary>
/// Activates approved tariff changes when their commencement date arrives, without anyone having to
/// call an endpoint. Runs once at startup (catching anything that came due while the API was down)
/// and then every minute. Like <see cref="Billing.BillingProcessingWorker"/> this is a simple
/// in-process poller; production should move to a durable scheduler with retry guarantees.
/// </summary>
public sealed class TariffActivationWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TariffActivationWorker> _logger;
    private readonly PrepaidEngine.Api.Health.WorkerStatusRegistry _status;
    private const string StatusName = "TariffActivationWorker";

    public TariffActivationWorker(IServiceScopeFactory scopeFactory, ILogger<TariffActivationWorker> logger, PrepaidEngine.Api.Health.WorkerStatusRegistry status)
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
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<TariffActivationService>();
                var result = await service.ActivateDueAsync(DateTime.UtcNow, stoppingToken);
                foreach (var a in result.Activated)
                    _logger.LogInformation("Activated tariff change request {RequestId} as tariff {TariffId}.", a.ChangeRequestId, a.NewTariffId);
                _status.Success(StatusName, 60);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // e.g. the database is not reachable yet; try again on the next tick.
                _logger.LogError(ex, "Tariff activation tick failed.");
                _status.Failure(StatusName, 60, ex);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
