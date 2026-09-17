using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Application.Billing;

/// <summary>
/// The DLP billing pipeline's core operations. Daily Load Profile (DLP) is the sole driver of
/// ongoing prepaid billing, split across two daily stages by when each meter's DLP was actually
/// received (see <see cref="DailyLoadProfile.ReceivedAt"/>):
///
/// <list type="bullet">
/// <item><description><b>Stage 1</b> (8:30-9:30 AM): bills every consumer whose DLP for the
/// previous day was received by 8:00 AM that morning.</description></item>
/// <item><description><b>Stage 2</b> (12:30-1:30 PM): bills every consumer whose DLP arrived
/// between 8:00 AM and 12:00 PM (missed Stage 1's cutoff), plus a <b>provisional</b> charge for
/// every consumer who still has no DLP for the previous day by the 12:00 PM cutoff.</description></item>
/// </list>
/// </summary>
public interface IBillingEngineService
{
    /// <summary>Ingests one Daily Load Profile — replaces an existing provisional profile for the
    /// same Consumer+Meter+Date if one exists, otherwise creates a new (real) profile. Validates
    /// the reading against the meter's previous day's closing reading before accepting it (see
    /// <see cref="DailyLoadProfileIngestResult"/>'s doc comment) — never billing a negative
    /// consumption sequence.</summary>
    Task<DailyLoadProfileIngestResult> IngestDailyLoadProfileAsync(
        DailyLoadProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stage 1 of the daily DLP charge for <paramref name="billingDate"/> (normally the
    /// previous calendar day): bills every prepaid consumer with an assigned tariff whose DLP for
    /// that date was received by 8:00 AM. Tracked under one <see cref="BillingRun"/>
    /// (<see cref="BillingRun.DlpStage1RunType"/>, unique per BillingDate).</summary>
    Task<IReadOnlyList<DailyProcessingResult>> ProcessDailyStage1Async(
        DateOnly billingDate, DateTime stage1CutoffUtc, CancellationToken cancellationToken = default);

    /// <summary>Stage 2 of the daily DLP charge for <paramref name="billingDate"/>: bills every
    /// remaining prepaid consumer whose DLP arrived between the Stage 1 cutoff and 12:00 PM, then
    /// posts a provisional charge (estimated from recent history) for every consumer who still has
    /// no DLP for that date at all. Tracked under one <see cref="BillingRun"/>
    /// (<see cref="BillingRun.DlpStage2RunType"/>, unique per BillingDate).</summary>
    Task<IReadOnlyList<DailyProcessingResult>> ProcessDailyStage2Async(
        DateOnly billingDate, DateTime stage2CutoffUtc, CancellationToken cancellationToken = default);

    /// <summary>Records a physical meter replacement for a consumer: creates the new
    /// <see cref="Domain.Entities.SmartMeter"/>, swaps it onto the consumer, and writes the
    /// <see cref="MeterAssignment"/> audit record — old and new meter cumulative readings are
    /// never compared against each other.</summary>
    Task<MeterAssignment> ReplaceMeterAsync(
        Guid consumerId, MeterReplacementRequest request, CancellationToken cancellationToken = default);
}
