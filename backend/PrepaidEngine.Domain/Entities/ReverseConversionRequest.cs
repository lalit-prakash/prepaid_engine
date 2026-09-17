using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A prepaid-to-postpaid conversion request — the reverse of <see cref="ConversionRequest"/>.
/// Unlike the forward direction, RMS never pushes this (see <see cref="ConversionRequest"/>'s own
/// doc comment), so there is no external decision to wait on: an operator requests it with a
/// mandatory reason, and it completes immediately once the safety checks in the conversion
/// endpoint pass (open billing hold, a conflicting pending forward conversion, or the consumer
/// already being Postpaid all reject it — see <c>POST /api/v1/conversions/reverse</c>). Still
/// kept as its own decision-trail entity, separate from <see cref="Consumer.ConvertToPostpaid"/>
/// itself, for the same audit reasons as the forward flow.
///
/// Captures the meter's final cumulative reading and the wallet's final balance at the moment of
/// conversion — the last two real facts about the consumer's prepaid life, since RMS's postpaid
/// billing cycle takes over from here and this project's own wallet/DLP billing stop.
/// </summary>
public class ReverseConversionRequest
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public string RequestedBy { get; private set; }
    public string Reason { get; private set; }
    public ReverseConversionStatus Status { get; private set; }
    public string? DecisionNote { get; private set; }
    public decimal? FinalMeterReadingKwh { get; private set; }
    public decimal? FinalWalletBalance { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public ReverseConversionRequest(Guid id, Guid consumerId, string requestedBy, string reason, DateTime requestedAt)
    {
        if (string.IsNullOrWhiteSpace(requestedBy))
            throw new ArgumentException("The requesting operator's identity is required.", nameof(requestedBy));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to request a prepaid-to-postpaid conversion.", nameof(reason));

        Id = id;
        ConsumerId = consumerId;
        RequestedBy = requestedBy;
        Reason = reason;
        RequestedAt = requestedAt;
        Status = ReverseConversionStatus.Requested;
    }

    // EF Core / serialization
    private ReverseConversionRequest()
    {
        RequestedBy = string.Empty;
        Reason = string.Empty;
    }

    public void Reject(string decisionNote, DateTime rejectedAt)
    {
        if (Status != ReverseConversionStatus.Requested)
            throw new InvalidOperationException($"Cannot reject a reverse conversion request that is {Status} — only a Requested request can be rejected.");
        if (string.IsNullOrWhiteSpace(decisionNote))
            throw new ArgumentException("A decision note is required to reject a reverse conversion request.", nameof(decisionNote));

        Status = ReverseConversionStatus.Rejected;
        DecisionNote = decisionNote;
        CompletedAt = rejectedAt;
    }

    /// <summary>Marks this request Completed — the caller must separately apply the real
    /// billing-mode change via <see cref="Consumer.ConvertToPostpaid"/> (same explicit two-step
    /// pattern as the forward flow's <see cref="ConversionRequest.Complete"/>).</summary>
    public void Complete(decimal finalMeterReadingKwh, decimal finalWalletBalance, DateTime completedAt)
    {
        if (Status != ReverseConversionStatus.Requested)
            throw new InvalidOperationException($"Cannot complete a reverse conversion request that is {Status} — only a Requested request can be completed.");

        Status = ReverseConversionStatus.Completed;
        FinalMeterReadingKwh = finalMeterReadingKwh;
        FinalWalletBalance = finalWalletBalance;
        CompletedAt = completedAt;
    }
}
