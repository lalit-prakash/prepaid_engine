using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.Connectivity;

/// <summary>Implements <see cref="IEmergencyCreditGuard"/> — see that interface's doc comment
/// for the full rule. Requires <paramref name="consumer"/>'s <see cref="Consumer.Wallet"/> to
/// already be loaded/tracked by the caller.</summary>
public class EmergencyCreditGuard : IEmergencyCreditGuard
{
    /// <summary>Reason recorded on a system-dispatched Disconnect command — also the marker this
    /// guard looks for before auto-reconnecting, so a manually-disconnected consumer (a different
    /// reason string) is never auto-reconnected just because their balance turned positive.</summary>
    public const string AutoDisconnectReason = "Automatic disconnection — wallet balance at or below the emergency credit limit.";

    public const string AutoReconnectReason = "Automatic reconnection — wallet balance restored to positive after a recharge/adjustment.";

    private readonly PrepaidEngineDbContext _db;
    private readonly IConnectivityCommandClient _connectivityClient;

    public EmergencyCreditGuard(PrepaidEngineDbContext db, IConnectivityCommandClient connectivityClient)
    {
        _db = db;
        _connectivityClient = connectivityClient;
    }

    public async Task EvaluateAsync(Consumer consumer, CancellationToken cancellationToken = default)
    {
        if (consumer.BillingMode != BillingMode.Prepaid)
            return;

        if (consumer.ConnectionStatus == ConnectionStatus.Active && !consumer.Wallet.IsWithinEmergencyCredit)
        {
            await DispatchAsync(consumer, ConnectivityCommandType.Disconnect, AutoDisconnectReason,
                NotificationEventType.AutoDisconnected,
                $"Prepaid supply for {consumer.AccountNumber} has been automatically disconnected — " +
                $"wallet balance Rs.{consumer.Wallet.Balance} is at or below the emergency credit limit.",
                cancellationToken);
            return;
        }

        if (consumer.ConnectionStatus == ConnectionStatus.Disconnected && consumer.Wallet.Balance > 0)
        {
            var lastDisconnect = await _db.ConnectivityCommands
                .Where(c => c.ConsumerId == consumer.Id && c.CommandType == ConnectivityCommandType.Disconnect
                    && c.Status == ConnectivityCommandStatus.Acknowledged)
                .OrderByDescending(c => c.AcknowledgedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastDisconnect is not null && lastDisconnect.Reason == AutoDisconnectReason)
            {
                await DispatchAsync(consumer, ConnectivityCommandType.Reconnect, AutoReconnectReason,
                    NotificationEventType.AutoReconnected,
                    $"Prepaid supply for {consumer.AccountNumber} has been automatically reconnected — " +
                    $"wallet balance is now Rs.{consumer.Wallet.Balance}.",
                    cancellationToken);
            }
        }
    }

    private async Task DispatchAsync(
        Consumer consumer, ConnectivityCommandType commandType, string reason,
        NotificationEventType notificationType, string notificationMessage, CancellationToken cancellationToken)
    {
        if (commandType == ConnectivityCommandType.Disconnect)
            consumer.RequestDisconnection();
        else
            consumer.RequestReconnection();

        var command = new ConnectivityCommand(Guid.NewGuid(), consumer.Id, commandType, reason, DateTime.UtcNow);
        _db.ConnectivityCommands.Add(command);
        command.MarkSent(DateTime.UtcNow);

        var correlationId = $"auto-{commandType.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}";
        var result = await _connectivityClient.SendConnectivityCommandAsync(
            new SendConnectivityCommandRequest(consumer.Id, commandType, correlationId), cancellationToken);

        switch (result.Outcome)
        {
            case ConnectivityCommandOutcome.Acknowledged:
                command.MarkAcknowledged(DateTime.UtcNow);
                if (commandType == ConnectivityCommandType.Disconnect)
                    consumer.Disconnect();
                else
                    consumer.Reconnect();

                _db.NotificationEvents.Add(new NotificationEvent(Guid.NewGuid(), consumer.Id, notificationType, notificationMessage, DateTime.UtcNow));
                break;
            case ConnectivityCommandOutcome.Failed:
                command.MarkFailed(result.Message ?? $"Meter rejected the automatic {commandType} command.");
                _db.OperationalExceptions.Add(new OperationalException(
                    Guid.NewGuid(), OperationalExceptionSourceType.ConnectivityCommand, command.Id, consumer.Id,
                    $"Automatic {commandType} command {command.Id} failed: {command.ErrorMessage}", DateTime.UtcNow));
                break;
            case ConnectivityCommandOutcome.TimedOut:
                command.MarkTimedOut();
                _db.OperationalExceptions.Add(new OperationalException(
                    Guid.NewGuid(), OperationalExceptionSourceType.ConnectivityCommand, command.Id, consumer.Id,
                    $"Automatic {commandType} command {command.Id} timed out waiting for meter acknowledgement.", DateTime.UtcNow));
                break;
        }
    }
}
