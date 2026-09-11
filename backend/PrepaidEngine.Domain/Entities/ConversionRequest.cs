using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A postpaid-to-prepaid conversion request as pushed by RMS to AMISP MDM/Prepaid engine, per
/// "Prepaid_Integration_Requirement_Document_ProposalFromAMISP_V1.1" section 1. Deliberately a
/// separate entity/lifecycle from <see cref="Consumer.BillingMode"/> — the consumer's own field
/// records the *current* fact, while this entity records the *decision trail* that got it there
/// (requested, approved/rejected, completed), the same intent-vs-execution split this project
/// already applies to RC/DC and meter credit. The spec only ever pushes this direction (postpaid
/// -> prepaid) — there is no RMS-initiated reverse flow, so unlike RC/DC's Disconnect/Reconnect
/// this entity has no "type" field, and <see cref="Consumer.ConvertToPostpaid"/> exists purely as
/// a symmetrical domain operation, not something this request models.
///
/// Only <see cref="ConversionStatus.Completed"/> means the consumer's billing mode actually
/// changed — reaching it requires calling <see cref="Consumer.ConvertToPrepaid"/> explicitly
/// (never a raw property set), matching this project's "no fake success states" rule.
/// </summary>
public class ConversionRequest
{
    /// <summary>Emergency-credit threshold (Rs.) that normally triggers disconnection — see
    /// <see cref="PrepaidWallet"/>/the disconnect endpoint.</summary>
    public const decimal NormalDisconnectThreshold = -200m;

    /// <summary>Emergency-credit threshold (Rs.) that applies instead of
    /// <see cref="NormalDisconnectThreshold"/> while a consumer is within
    /// <see cref="GracePeriodEndDate"/> of this conversion (spec section 1: "Disconnection
    /// Exemption after the conversion phase").</summary>
    public const decimal GracePeriodDisconnectThreshold = -300m;

    /// <summary>Working days (Mon-Fri) of grace after <see cref="ConversionDate"/> during which
    /// <see cref="GracePeriodDisconnectThreshold"/> applies instead of the normal threshold. The
    /// spec does not model public holidays here (that gap is separately noted for Happy Hours) —
    /// "working day" below means Mon-Fri only.</summary>
    public const int GracePeriodWorkingDays = 5;

    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }

    /// <summary>RMS's own transaction identifier for this request — echoed back verbatim in the
    /// Success/Fail acknowledgement (spec section 1).</summary>
    public string TransactionId { get; private set; }
    public string MeterSerialNumber { get; private set; }

    /// <summary>RMS's consumer number. This project's <see cref="Consumer.AccountNumber"/> plays
    /// the same role — see the conversion endpoint for how the two are matched.</summary>
    public string ConsumerNumber { get; private set; }

    /// <summary>Request(meter) Type — "PRE" by default per spec.</summary>
    public string RequestType { get; private set; }
    public ConversionConsumerType ConsumerType { get; private set; }

    /// <summary>Reading the last post-paid bill was based on (1st day of the conversion month,
    /// 00:00 hrs) — the starting point for the first prepaid bill's consumption calculation.</summary>
    public decimal InitialReading { get; private set; }
    public DateTime InitialReadingDateTime { get; private set; }

    /// <summary>The day RMS pushed this request — usually the 10th of the month, after the prior
    /// month's post-paid billing completes. <see cref="GracePeriodEndDate"/> is measured from
    /// this date, not from <see cref="RequestedAt"/> (which is when AMISP received it).</summary>
    public DateTime ConversionDate { get; private set; }

    public ConversionStatus Status { get; private set; }
    public string? DecisionNote { get; private set; }

    public DateTime RequestedAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public ConversionRequest(
        Guid id,
        Guid consumerId,
        string transactionId,
        string meterSerialNumber,
        string consumerNumber,
        ConversionConsumerType consumerType,
        decimal initialReading,
        DateTime initialReadingDateTime,
        DateTime conversionDate,
        DateTime requestedAt,
        string requestType = "PRE")
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            throw new ArgumentException("A transaction ID is required.", nameof(transactionId));
        if (string.IsNullOrWhiteSpace(meterSerialNumber))
            throw new ArgumentException("A meter serial number is required.", nameof(meterSerialNumber));
        if (string.IsNullOrWhiteSpace(consumerNumber))
            throw new ArgumentException("A consumer number is required.", nameof(consumerNumber));
        if (string.IsNullOrWhiteSpace(requestType))
            throw new ArgumentException("A request type is required.", nameof(requestType));
        if (initialReading < 0)
            throw new ArgumentOutOfRangeException(nameof(initialReading), "Initial reading cannot be negative.");

        Id = id;
        ConsumerId = consumerId;
        TransactionId = transactionId;
        MeterSerialNumber = meterSerialNumber;
        ConsumerNumber = consumerNumber;
        RequestType = requestType;
        ConsumerType = consumerType;
        InitialReading = initialReading;
        InitialReadingDateTime = initialReadingDateTime;
        ConversionDate = conversionDate;
        Status = ConversionStatus.Requested;
        RequestedAt = requestedAt;
    }

    // EF Core / serialization
    private ConversionRequest()
    {
        TransactionId = string.Empty;
        MeterSerialNumber = string.Empty;
        ConsumerNumber = string.Empty;
        RequestType = "PRE";
    }

    /// <summary>Last calendar day of <see cref="GracePeriodWorkingDays"/> working days (Mon-Fri)
    /// counted forward from <see cref="ConversionDate"/>.</summary>
    public DateTime GracePeriodEndDate
    {
        get
        {
            var date = ConversionDate.Date;
            var remaining = GracePeriodWorkingDays;
            while (remaining > 0)
            {
                date = date.AddDays(1);
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    remaining--;
            }
            return date;
        }
    }

    /// <summary>True if <paramref name="asOf"/> falls on/before <see cref="GracePeriodEndDate"/>
    /// — while true, disconnection logic must use <see cref="GracePeriodDisconnectThreshold"/>
    /// instead of <see cref="NormalDisconnectThreshold"/> for this request's consumer.</summary>
    public bool IsWithinGracePeriod(DateTime asOf) => asOf.Date <= GracePeriodEndDate;

    public void Approve(DateTime approvedAt)
    {
        if (Status != ConversionStatus.Requested)
            throw new InvalidOperationException($"Cannot approve a conversion request that is {Status} — only a Requested request can be approved.");

        Status = ConversionStatus.Approved;
        DecidedAt = approvedAt;
    }

    public void Reject(string reason, DateTime rejectedAt)
    {
        if (Status != ConversionStatus.Requested)
            throw new InvalidOperationException($"Cannot reject a conversion request that is {Status} — only a Requested request can be rejected.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to reject a conversion request.", nameof(reason));

        Status = ConversionStatus.Rejected;
        DecisionNote = reason;
        DecidedAt = rejectedAt;
    }

    /// <summary>Marks the request Completed once the caller has already applied the real
    /// billing-mode change via <see cref="Consumer.ConvertToPrepaid"/> — this method itself never
    /// touches <see cref="Consumer"/>, keeping the decision-trail entity and the consumer-state
    /// change as two explicit steps a caller must both take.</summary>
    public void Complete(DateTime completedAt)
    {
        if (Status != ConversionStatus.Approved)
            throw new InvalidOperationException($"Cannot complete a conversion request that is {Status} — only an Approved request can be completed.");

        Status = ConversionStatus.Completed;
        CompletedAt = completedAt;
    }
}
