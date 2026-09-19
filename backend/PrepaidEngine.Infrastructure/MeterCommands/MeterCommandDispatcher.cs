using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.MeterCommands;

/// <summary>
/// Sends queued meter credit commands to the meter/MDM layer, so a recharge request never waits on it.
///
/// A recharge writes its wallet credit and a <c>Queued</c> <see cref="MeterCommand"/> in one transaction (the
/// command row is the outbox). This dispatcher claims queued commands one at a time with a conditional
/// update, so several instances can run it without sending a command twice, sends each with a key that is
/// stable for that attempt (a resend after a crash is safe), and records the outcome. Failed and timed-out
/// commands raise an operator exception as before; they are retried by an operator, not automatically.
/// A command left <c>Sent</c> with no outcome (its sender died) is marked timed out after a while.
/// </summary>
public class MeterCommandDispatcher
{
    private readonly PrepaidEngineDbContext _db;
    private readonly IMeterCommandClient _client;
    private readonly ILogger<MeterCommandDispatcher> _logger;

    public MeterCommandDispatcher(PrepaidEngineDbContext db, IMeterCommandClient client, ILogger<MeterCommandDispatcher> logger)
    {
        _db = db;
        _client = client;
        _logger = logger;
    }

    /// <summary>Sends up to <paramref name="batchSize"/> queued commands, oldest first. Returns how many this call sent.</summary>
    public async Task<int> DispatchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var ids = await _db.MeterCommands.AsNoTracking()
            .Where(m => m.Status == MeterCommandStatus.Queued)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var id in ids)
        {
            if (await DispatchOneAsync(id, cancellationToken)) sent++;
        }
        return sent;
    }

    private async Task<bool> DispatchOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // Claim: only one instance can move this command from Queued to Sent.
        var claimed = await _db.MeterCommands
            .Where(m => m.Id == id && m.Status == MeterCommandStatus.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MeterCommandStatus.Sent).SetProperty(m => m.SentAt, now), cancellationToken);
        if (claimed != 1) return false;

        var command = await _db.MeterCommands.FirstAsync(m => m.Id == id, cancellationToken);
        try
        {
            var result = await _client.SendCreditCommandAsync(
                new SendCreditCommandRequest(command.ConsumerId, command.CreditAmount, $"meter-credit:{command.Id:N}:{command.RetryCount}"),
                cancellationToken);
            command.RecordExternalCommandId(result.ExternalCommandId);

            switch (result.Outcome)
            {
                case MeterCommandOutcome.Acknowledged:
                    command.MarkAcknowledged(DateTime.UtcNow, result.ResponseCode, result.Message);
                    break;
                case MeterCommandOutcome.Failed:
                    command.MarkFailed(result.Message ?? "Meter rejected the credit command.", result.ResponseCode);
                    RaiseException(command, $"Meter command {command.Id} failed{RetryNote(command)}: {command.ErrorMessage}");
                    break;
                case MeterCommandOutcome.TimedOut:
                    command.MarkTimedOut();
                    RaiseException(command, $"Meter command {command.Id} timed out waiting for meter acknowledgement{RetryNote(command)}.");
                    break;
            }
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The command stays Sent; if no outcome is ever recorded it is timed out by RecoverStuckAsync.
            _logger.LogError(ex, "Sending meter command {CommandId} failed unexpectedly.", id);
            _db.ChangeTracker.Clear();
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
        return true;
    }

    /// <summary>Times out commands that were sent but never got an outcome, raising an operator exception for each.</summary>
    public async Task<int> RecoverStuckAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var stuck = await _db.MeterCommands
            .Where(m => m.Status == MeterCommandStatus.Sent && m.SentAt != null && m.SentAt < cutoff)
            .OrderBy(m => m.SentAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var command in stuck)
        {
            command.MarkTimedOut();
            RaiseException(command, $"Meter command {command.Id} was sent but no outcome was recorded (the sender may have stopped). " +
                                    "It is not known whether the meter was credited; check the meter before retrying.");
        }
        if (stuck.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        return stuck.Count;
    }

    private void RaiseException(MeterCommand command, string description)
        => _db.OperationalExceptions.Add(new OperationalException(
            Guid.NewGuid(), OperationalExceptionSourceType.MeterCommand, command.Id, command.ConsumerId, description, DateTime.UtcNow));

    private static string RetryNote(MeterCommand command) => command.RetryCount > 0 ? $" on retry #{command.RetryCount}" : string.Empty;
}
