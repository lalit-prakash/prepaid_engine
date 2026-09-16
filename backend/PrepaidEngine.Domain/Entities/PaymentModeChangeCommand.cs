using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// The command that actually changes a meter's payment mode from postpaid to prepaid, dispatched
/// by MDMS down the MDMS → HES → Meter chain once a <see cref="ConversionRequest"/> is approved.
/// The meter's own acknowledgement then travels back Meter → HES → MDMS. Deliberately a separate
/// entity/lifecycle from <see cref="ConversionRequest.Status"/> — the request records the
/// business decision trail (Requested → Approved/Rejected → Completed), while this entity tracks
/// whether the physical meter actually acted on it, the same command/acknowledgement split this
/// project already applies to meter credit and RC/DC (see <see cref="MeterCommand"/> and
/// <see cref="ConnectivityCommand"/>'s doc comments for the identical reasoning). Only
/// <see cref="PaymentModeChangeStatus.Acknowledged"/> is the real signal the meter's payment mode
/// changed — <see cref="ConversionRequest.Complete"/>/<see cref="Consumer.ConvertToPrepaid"/> must
/// never run ahead of it.
///
/// Says nothing about *how* the command reaches the meter (no real MDMS/HES integration exists in
/// this project) — only a mock simulator implementation is registered, mirroring
/// <see cref="MeterCommand"/>/<see cref="ConnectivityCommand"/>'s own IRmsClient/
/// IMeterCommandClient/IConnectivityCommandClient pattern.
/// </summary>
public class PaymentModeChangeCommand
{
    public Guid Id { get; private set; }
    public Guid ConversionRequestId { get; private set; }
    public Guid ConsumerId { get; private set; }
    public PaymentModeChangeStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>The meter's cumulative reading (kWh) at the moment it acknowledged the payment
    /// mode change — the current-reading half of the conversion opening-bill calculation
    /// (<see cref="ConversionRequest.InitialReading"/> is the other half). Only ever set once,
    /// by <see cref="MarkAcknowledged"/>.</summary>
    public decimal? MeterReadingAtConversion { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public DateTime? AcknowledgedAt { get; private set; }

    public PaymentModeChangeCommand(Guid id, Guid conversionRequestId, Guid consumerId, DateTime createdAt)
    {
        Id = id;
        ConversionRequestId = conversionRequestId;
        ConsumerId = consumerId;
        Status = PaymentModeChangeStatus.Queued;
        CreatedAt = createdAt;
    }

    // EF Core / serialization
    private PaymentModeChangeCommand()
    {
    }

    /// <summary>The command left MDMS for the HES/meter layer. Not yet a success — see
    /// <see cref="MarkAcknowledged"/> for the only real completion.</summary>
    public void MarkSent(DateTime sentAt)
    {
        if (Status != PaymentModeChangeStatus.Queued)
            throw new InvalidOperationException($"Cannot mark a {Status} command as sent.");

        Status = PaymentModeChangeStatus.Sent;
        SentAt = sentAt;
    }

    /// <summary>The only state that means the meter actually switched to prepaid mode — a real
    /// downstream acknowledgement relayed back through HES, never inferred from the request's
    /// own status alone.</summary>
    public void MarkAcknowledged(DateTime acknowledgedAt, decimal meterReadingAtConversion)
    {
        if (Status != PaymentModeChangeStatus.Sent)
            throw new InvalidOperationException($"Cannot acknowledge a command that was never sent (current status: {Status}).");
        if (meterReadingAtConversion < 0)
            throw new ArgumentOutOfRangeException(nameof(meterReadingAtConversion), "Meter reading cannot be negative.");

        Status = PaymentModeChangeStatus.Acknowledged;
        AcknowledgedAt = acknowledgedAt;
        MeterReadingAtConversion = meterReadingAtConversion;
    }

    /// <summary>The HES/meter layer explicitly rejected or errored on this command.</summary>
    public void MarkFailed(string errorMessage)
    {
        if (Status is PaymentModeChangeStatus.Acknowledged or PaymentModeChangeStatus.Failed or PaymentModeChangeStatus.TimedOut)
            throw new InvalidOperationException($"Cannot mark a {Status} command as failed.");
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("An error message is required to mark a command failed.", nameof(errorMessage));

        Status = PaymentModeChangeStatus.Failed;
        ErrorMessage = errorMessage;
    }

    /// <summary>No acknowledgement arrived within the expected window — distinct from
    /// <see cref="MarkFailed"/> so operators can tell "it errored" apart from "we never heard
    /// back".</summary>
    public void MarkTimedOut()
    {
        if (Status != PaymentModeChangeStatus.Sent)
            throw new InvalidOperationException($"Cannot time out a command that was never sent (current status: {Status}).");

        Status = PaymentModeChangeStatus.TimedOut;
    }
}
