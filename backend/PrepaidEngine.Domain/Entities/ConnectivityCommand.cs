using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A remote disconnect or reconnect command dispatched to a consumer's meter. Deliberately a
/// separate entity/lifecycle from <see cref="Consumer.ConnectionStatus"/>: the consumer's own
/// status (including its <c>DisconnectionPending</c>/<c>ReconnectionPending</c> values) records
/// local *intent*, while this entity tracks whether the physical meter actually acted on that
/// intent — the same command/acknowledgement split this project already applies to meter
/// credit (see <see cref="MeterCommand"/>'s doc comment for the identical reasoning). A command
/// reaching <see cref="ConnectivityCommandStatus.Acknowledged"/> is the only real signal the
/// meter's connection state changed; nothing else may be presented as that.
///
/// Says nothing about *how* a command reaches the meter (STS/DLMS/COSEM/vendor API) — no such
/// integration exists in this project yet, matching <see cref="MeterCommand"/> and
/// <c>RechargeTransaction</c>'s own IRmsClient/IMeterCommandClient pattern; a future
/// <c>IConnectivityCommandClient</c> would plug in the same way when this gets wired into a
/// workflow.
///
/// A reason is mandatory (not optional metadata): the "critical command confirmation" rule
/// from the original UI/UX request treats disconnect/reconnect as destructive operator actions
/// that must always carry an auditable justification, never a bare on/off toggle.
/// </summary>
public class ConnectivityCommand
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public ConnectivityCommandType CommandType { get; private set; }
    public string Reason { get; private set; }
    public ConnectivityCommandStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? AcknowledgedAt { get; private set; }

    public ConnectivityCommand(Guid id, Guid consumerId, ConnectivityCommandType commandType, string reason, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for a disconnect/reconnect command.", nameof(reason));

        Id = id;
        ConsumerId = consumerId;
        CommandType = commandType;
        Reason = reason;
        Status = ConnectivityCommandStatus.Queued;
        RetryCount = 0;
        CreatedAt = createdAt;
    }

    // EF Core / serialization
    private ConnectivityCommand()
    {
        Reason = string.Empty;
    }

    /// <summary>The command left the Prepaid Engine for the meter-command layer. Not yet a
    /// success — see <see cref="MarkAcknowledged"/> for the only real completion.</summary>
    public void MarkSent(DateTime sentAt)
    {
        if (Status != ConnectivityCommandStatus.Queued)
            throw new InvalidOperationException($"Cannot mark a {Status} command as sent.");

        Status = ConnectivityCommandStatus.Sent;
        SentAt = sentAt;
    }

    /// <summary>The only state that means the meter's physical connection actually changed — a
    /// real downstream acknowledgement, never inferred from the consumer's pending status alone.</summary>
    public void MarkAcknowledged(DateTime acknowledgedAt)
    {
        if (Status != ConnectivityCommandStatus.Sent)
            throw new InvalidOperationException($"Cannot acknowledge a command that was never sent (current status: {Status}).");

        Status = ConnectivityCommandStatus.Acknowledged;
        AcknowledgedAt = acknowledgedAt;
    }

    /// <summary>The meter-command layer explicitly rejected or errored on this command.</summary>
    public void MarkFailed(string errorMessage)
    {
        if (Status is ConnectivityCommandStatus.Acknowledged or ConnectivityCommandStatus.Failed or ConnectivityCommandStatus.TimedOut)
            throw new InvalidOperationException($"Cannot mark a {Status} command as failed.");
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("An error message is required to mark a command failed.", nameof(errorMessage));

        Status = ConnectivityCommandStatus.Failed;
        ErrorMessage = errorMessage;
    }

    /// <summary>No acknowledgement arrived within the expected window — distinct from
    /// <see cref="MarkFailed"/> (an explicit rejection) so operators can tell "it errored" apart
    /// from "we never heard back", which usually call for different remediation.</summary>
    public void MarkTimedOut()
    {
        if (Status != ConnectivityCommandStatus.Sent)
            throw new InvalidOperationException($"Cannot time out a command that was never sent (current status: {Status}).");

        Status = ConnectivityCommandStatus.TimedOut;
    }

    /// <summary>Resets a failed/timed-out command back to Queued for reprocessing, incrementing
    /// the retry count so operators/automation can see how many attempts a command has taken.</summary>
    public void Retry()
    {
        if (Status is not (ConnectivityCommandStatus.Failed or ConnectivityCommandStatus.TimedOut))
            throw new InvalidOperationException($"Cannot retry a command that is {Status} — only Failed or TimedOut commands can be retried.");

        Status = ConnectivityCommandStatus.Queued;
        RetryCount++;
        ErrorMessage = null;
        SentAt = null;
    }
}
