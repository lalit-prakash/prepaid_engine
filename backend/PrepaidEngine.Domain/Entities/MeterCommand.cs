using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A meter credit command — the step that actually updates the smart meter's available credit
/// after RMS has confirmed a recharge payment. This is deliberately a separate entity/lifecycle
/// from <see cref="RechargeTransaction"/>, not a status on it: RMS confirming payment and the
/// meter itself being credited are two different systems succeeding or failing independently,
/// and collapsing them into one status would violate the "never represent Recharge Successful
/// as Meter Credit Successful" rule from the UI/UX request this project is building toward.
///
/// This entity models the command/acknowledgement lifecycle only — it intentionally says
/// nothing about *how* a command reaches the meter (STS token generation, DLMS/COSEM push, a
/// meter vendor's own HTTP/MQTT API, etc.). No such integration exists in this project yet; see
/// <c>IMeterCommandClient</c> for the abstraction a real adapter would implement, mirroring how
/// <see cref="RechargeTransaction"/> relates to <c>IRmsClient</c>.
/// </summary>
public class MeterCommand
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }

    /// <summary>The recharge this credit command exists to fulfil. Every meter credit command
    /// traces back to exactly one RMS-confirmed recharge — there is no standalone credit.</summary>
    public Guid RechargeTransactionId { get; private set; }

    public decimal CreditAmount { get; private set; }
    public MeterCommandStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? AcknowledgedAt { get; private set; }

    /// <summary>The external command/tracking id the meter-command layer (MDM/HES/vendor API)
    /// assigned to this dispatch, if it returned one — kept for operational investigation
    /// ("which command did the downstream system actually see"). Null until <see cref="MarkSent"/>.</summary>
    public string? ExternalCommandId { get; private set; }

    /// <summary>The downstream response code/message, if the meter-command layer returned one —
    /// distinct from <see cref="ErrorMessage"/> (this project's own summary) so the raw
    /// downstream response stays available for investigation even on a successful outcome.</summary>
    public string? ResponseCode { get; private set; }
    public string? ResponseMessage { get; private set; }

    public MeterCommand(Guid id, Guid consumerId, Guid rechargeTransactionId, decimal creditAmount, DateTime createdAt)
    {
        if (creditAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(creditAmount), "Credit amount must be positive.");

        Id = id;
        ConsumerId = consumerId;
        RechargeTransactionId = rechargeTransactionId;
        CreditAmount = creditAmount;
        Status = MeterCommandStatus.Queued;
        RetryCount = 0;
        CreatedAt = createdAt;
    }

    // EF Core / serialization
    private MeterCommand()
    {
    }

    /// <summary>The command left the Prepaid Engine for the meter-command layer. Not yet a
    /// success — see <see cref="MarkAcknowledged"/> for the only real completion.</summary>
    public void MarkSent(DateTime sentAt, string? externalCommandId = null)
    {
        if (Status != MeterCommandStatus.Queued)
            throw new InvalidOperationException($"Cannot mark a {Status} command as sent.");

        Status = MeterCommandStatus.Sent;
        SentAt = sentAt;
        ExternalCommandId = externalCommandId;
    }

    /// <summary>The only state that means the meter was actually credited — a real downstream
    /// acknowledgement, never inferred from RMS payment confirmation alone.</summary>
    public void MarkAcknowledged(DateTime acknowledgedAt, string? responseCode = null, string? responseMessage = null)
    {
        if (Status != MeterCommandStatus.Sent)
            throw new InvalidOperationException($"Cannot acknowledge a command that was never sent (current status: {Status}).");

        Status = MeterCommandStatus.Acknowledged;
        AcknowledgedAt = acknowledgedAt;
        ResponseCode = responseCode;
        ResponseMessage = responseMessage;
    }

    /// <summary>The meter-command layer explicitly rejected or errored on this command.</summary>
    public void MarkFailed(string errorMessage, string? responseCode = null)
    {
        if (Status is MeterCommandStatus.Acknowledged or MeterCommandStatus.Failed or MeterCommandStatus.TimedOut)
            throw new InvalidOperationException($"Cannot mark a {Status} command as failed.");
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("An error message is required to mark a command failed.", nameof(errorMessage));

        Status = MeterCommandStatus.Failed;
        ErrorMessage = errorMessage;
        ResponseCode = responseCode;
        ResponseMessage = errorMessage;
    }

    /// <summary>No acknowledgement arrived within the expected window — distinct from
    /// <see cref="MarkFailed"/> (an explicit rejection) so operators can tell "it errored" apart
    /// from "we never heard back", which usually call for different remediation.</summary>
    public void MarkTimedOut()
    {
        if (Status != MeterCommandStatus.Sent)
            throw new InvalidOperationException($"Cannot time out a command that was never sent (current status: {Status}).");

        Status = MeterCommandStatus.TimedOut;
    }

    /// <summary>Resets a failed/timed-out command back to Queued for reprocessing, incrementing
    /// the retry count so operators/automation can see how many attempts a command has taken.</summary>
    public void Retry()
    {
        if (Status is not (MeterCommandStatus.Failed or MeterCommandStatus.TimedOut))
            throw new InvalidOperationException($"Cannot retry a command that is {Status} — only Failed or TimedOut commands can be retried.");

        Status = MeterCommandStatus.Queued;
        RetryCount++;
        ErrorMessage = null;
        SentAt = null;
    }
}
