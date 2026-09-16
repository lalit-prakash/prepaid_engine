using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Application.Billing;

/// <summary>
/// The DLP billing pipeline's core operations. Daily Load Profile (DLP, created at the 00:00
/// boundary) is the sole driver of ongoing prepaid billing — the hourly Load Survey (LS) pipeline
/// that used to also debit the wallet, and reconcile against DLP via a settlement adjustment, has
/// been removed: DLP alone now posts one direct daily charge per consumer.
/// </summary>
public interface IBillingEngineService
{
    /// <summary>Ingests one Daily Load Profile — replaces an existing provisional profile for the
    /// same Consumer+Meter+Date if one exists, otherwise creates a new (real) profile.</summary>
    Task<DailyLoadProfileIngestResult> IngestDailyLoadProfileAsync(
        DailyLoadProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Processes the daily DLP charge for <paramref name="billingDate"/> across every
    /// prepaid consumer with an assigned tariff: computes the day's charge from the DLP's total
    /// kWh (or creates+charges a provisional profile estimated from recent history if none
    /// arrived), and posts one direct wallet debit. Tracked under one <see cref="BillingRun"/>
    /// (unique per RunType+BillingDate). After each consumer's charge is posted, evaluates
    /// whether their wallet has crossed the emergency-credit threshold and auto-dispatches a
    /// disconnect (or reconnect, on the recharge side) accordingly — see
    /// <see cref="Connectivity.IEmergencyCreditGuard"/>.</summary>
    Task<IReadOnlyList<DailyProcessingResult>> ProcessDailyAsync(
        DateOnly billingDate, CancellationToken cancellationToken = default);

    /// <summary>Records a physical meter replacement for a consumer: creates the new
    /// <see cref="Domain.Entities.SmartMeter"/>, swaps it onto the consumer, and writes the
    /// <see cref="MeterAssignment"/> audit record — old and new meter cumulative readings are
    /// never compared against each other.</summary>
    Task<MeterAssignment> ReplaceMeterAsync(
        Guid consumerId, MeterReplacementRequest request, CancellationToken cancellationToken = default);
}
