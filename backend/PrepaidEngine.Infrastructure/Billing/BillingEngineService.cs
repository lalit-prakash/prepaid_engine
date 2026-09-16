using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.Billing;

/// <summary>
/// Implements <see cref="IBillingEngineService"/> directly against
/// <see cref="PrepaidEngineDbContext"/>. Daily Load Profile (DLP) alone drives ongoing prepaid
/// billing — the Load Survey (LS) hourly pipeline this class used to also run (and reconcile DLP
/// against) has been removed; each day's DLP total kWh now posts one direct wallet debit.
/// </summary>
public class BillingEngineService : IBillingEngineService
{
    /// <summary>Demo low-balance notification threshold. Production values must be
    /// configuration, not a constant — documented as a known follow-up.</summary>
    private const decimal LowBalanceThreshold = 100m;

    /// <summary>Demo provisional-DLP estimation window: average of up to this many previous
    /// valid DLP records for the consumer/meter.</summary>
    private const int ProvisionalEstimationWindow = 7;

    private readonly PrepaidEngineDbContext _db;
    private readonly IEmergencyCreditGuard _emergencyCreditGuard;

    public BillingEngineService(PrepaidEngineDbContext db, IEmergencyCreditGuard emergencyCreditGuard)
    {
        _db = db;
        _emergencyCreditGuard = emergencyCreditGuard;
    }

    public async Task<DailyLoadProfileIngestResult> IngestDailyLoadProfileAsync(
        DailyLoadProfileRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _db.DailyLoadProfiles.FirstOrDefaultAsync(
            d => d.ConsumerId == request.ConsumerId && d.MeterId == request.MeterId && d.ProfileDate == request.ProfileDate,
            cancellationToken);

        if (existing is not null)
        {
            if (!existing.IsProvisional)
            {
                return new DailyLoadProfileIngestResult(existing.Id, existing.Status.ToString(), false,
                    "A non-provisional DLP already exists for this consumer/meter/date.");
            }

            try
            {
                existing.ReplaceWithActual(request.StartCumulativeKwh, request.EndCumulativeKwh, request.GeneratedAt, request.SourceReference);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return new DailyLoadProfileIngestResult(existing.Id, existing.Status.ToString(), false, ex.Message);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return new DailyLoadProfileIngestResult(existing.Id, existing.Status.ToString(), true, "Actual DLP replaced the provisional profile.");
        }

        DailyLoadProfile profile;
        try
        {
            profile = new DailyLoadProfile(
                Guid.NewGuid(), request.ConsumerId, request.MeterId, request.ProfileDate, request.GeneratedAt,
                request.StartCumulativeKwh, request.EndCumulativeKwh, isProvisional: false, request.SourceReference);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new DailyLoadProfileIngestResult(Guid.Empty, "Rejected", false, ex.Message);
        }

        profile.MarkValidated();
        _db.DailyLoadProfiles.Add(profile);
        await _db.SaveChangesAsync(cancellationToken);
        return new DailyLoadProfileIngestResult(profile.Id, profile.Status.ToString(), false, null);
    }

    public async Task<IReadOnlyList<DailyProcessingResult>> ProcessDailyAsync(
        DateOnly billingDate, CancellationToken cancellationToken = default)
    {
        var existingRun = await _db.BillingRuns.FirstOrDefaultAsync(
            r => r.RunType == BillingRun.DlpDailyRunType && r.BillingDate == billingDate, cancellationToken);
        if (existingRun is not null)
        {
            return new List<DailyProcessingResult>
            {
                new(Guid.Empty, false, false, 0, true, $"A daily billing run already exists for {billingDate:yyyy-MM-dd} (status {existingRun.Status}).")
            };
        }

        var run = new BillingRun(Guid.NewGuid(), BillingRun.DlpDailyRunType, billingDate, DateTime.UtcNow);
        _db.BillingRuns.Add(run);

        var results = new List<DailyProcessingResult>();

        var consumers = await _db.Consumers
            .Include(c => c.Meter)
            .Include(c => c.Wallet).ThenInclude(w => w.Transactions)
            .Where(c => c.BillingMode == BillingMode.Prepaid && c.TariffId != null)
            .ToListAsync(cancellationToken);

        foreach (var consumer in consumers)
        {
            run.RecordConsumer();

            var control = await _db.MeterBillingControls.FirstOrDefaultAsync(
                c => c.ConsumerId == consumer.Id && c.ActualBillingBlocked, cancellationToken);
            if (control is not null)
            {
                results.Add(new DailyProcessingResult(consumer.Id, false, false, 0, true,
                    "Actual DLP billing is on hold for this meter."));
                continue;
            }

            var tariff = await _db.Tariffs.FirstOrDefaultAsync(t => t.Id == consumer.TariffId, cancellationToken);
            if (tariff is null)
            {
                run.RecordException();
                results.Add(new DailyProcessingResult(consumer.Id, false, false, 0, true, "Assigned tariff could not be loaded."));
                continue;
            }

            var dlp = await _db.DailyLoadProfiles.FirstOrDefaultAsync(
                d => d.ConsumerId == consumer.Id && d.MeterId == consumer.Meter.Id && d.ProfileDate == billingDate, cancellationToken);

            var dlpAvailable = dlp is not null && !dlp.IsProvisional;
            decimal chargeAmount;
            string reference;

            if (dlp is null)
            {
                // Missing DLP: create a provisional profile estimated from recent valid history
                // rather than treating the missing day as zero consumption.
                var recentValid = await _db.DailyLoadProfiles
                    .Where(d => d.ConsumerId == consumer.Id && d.MeterId == consumer.Meter.Id && !d.IsProvisional)
                    .OrderByDescending(d => d.ProfileDate)
                    .Take(ProvisionalEstimationWindow)
                    .ToListAsync(cancellationToken);
                var estimatedKwh = recentValid.Count > 0 ? recentValid.Average(d => d.TotalKwh) : 0m;

                dlp = DailyLoadProfile.CreateProvisional(Guid.NewGuid(), consumer.Id, consumer.Meter.Id, billingDate, DateTime.UtcNow, estimatedKwh);
                _db.DailyLoadProfiles.Add(dlp);

                chargeAmount = CalculateDailyCharge(tariff, consumer, estimatedKwh);
                reference = $"DLP-PROV:{dlp.Id}";

                if (chargeAmount > 0)
                {
                    var provisionalDebit = consumer.Wallet.Debit(chargeAmount, WalletTransactionType.DailyDlpCharge, reference);
                    _db.WalletTransactions.Add(provisionalDebit);
                }

                await RaiseProvisionalNotificationAsync(consumer, cancellationToken);
                await RaiseCreditNotificationsAsync(consumer, cancellationToken);
                await _emergencyCreditGuard.EvaluateAsync(consumer, cancellationToken);

                results.Add(new DailyProcessingResult(consumer.Id, false, true, chargeAmount, false, null));
                continue;
            }

            if (dlp.TotalKwh < 0)
            {
                run.RecordException();
                results.Add(new DailyProcessingResult(consumer.Id, true, dlp.IsProvisional, 0, true, "DLP has negative total consumption."));
                continue;
            }

            reference = $"DLP:{dlp.Id}";
            var alreadyBilled = consumer.Wallet.Transactions.Any(t => t.Reference == reference);
            if (alreadyBilled || dlp.Status == DailyProfileStatus.Billed)
            {
                results.Add(new DailyProcessingResult(consumer.Id, true, dlp.IsProvisional, 0, true, "This DLP has already been billed."));
                continue;
            }

            chargeAmount = CalculateDailyCharge(tariff, consumer, dlp.TotalKwh);
            if (chargeAmount > 0)
            {
                var debit = consumer.Wallet.Debit(chargeAmount, WalletTransactionType.DailyDlpCharge, reference);
                _db.WalletTransactions.Add(debit);
            }

            dlp.MarkBilled();
            await RaiseCreditNotificationsAsync(consumer, cancellationToken);
            await _emergencyCreditGuard.EvaluateAsync(consumer, cancellationToken);

            results.Add(new DailyProcessingResult(consumer.Id, dlpAvailable, dlp.IsProvisional, chargeAmount, false, null));
        }

        run.Complete(DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return results;
    }

    public async Task<MeterAssignment> ReplaceMeterAsync(
        Guid consumerId, MeterReplacementRequest request, CancellationToken cancellationToken = default)
    {
        var consumer = await _db.Consumers.Include(c => c.Meter).FirstOrDefaultAsync(c => c.Id == consumerId, cancellationToken)
            ?? throw new InvalidOperationException($"Consumer {consumerId} not found.");

        var oldMeterId = consumer.Meter.Id;
        var newMeter = new SmartMeter(Guid.NewGuid(), request.NewMeterNumber, request.Phase);
        _db.Meters.Add(newMeter);

        // Deliberately never compares oldMeterClosingReadingKwh against newMeterOpeningReadingKwh
        // — they belong to two different physical meters (spec §15/§16).
        var assignment = new MeterAssignment(
            Guid.NewGuid(), consumerId, oldMeterId, newMeter.Id, MeterAssignmentEventType.Replaced,
            request.EffectiveFrom, request.OldMeterClosingReadingKwh, request.NewMeterOpeningReadingKwh,
            DateTime.UtcNow, request.Reason);
        _db.MeterAssignments.Add(assignment);

        consumer.ReplaceMeter(newMeter);

        await _db.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    private static decimal CalculateDailyCharge(Tariff tariff, Consumer consumer, decimal consumptionKwh)
    {
        var grossEnergyCharge = tariff.CalculateEnergyCharge(consumptionKwh);
        var rebate = grossEnergyCharge * (tariff.PrepaidEnergyRebatePercent / 100m);
        var netEnergyCharge = grossEnergyCharge - rebate;
        var dailyFixedCharge = tariff.CalculateDailyFixedCharge(consumer.ConnectedLoadKw);
        return Math.Round(netEnergyCharge + dailyFixedCharge, 2, MidpointRounding.AwayFromZero);
    }

    private async Task RaiseCreditNotificationsAsync(Consumer consumer, CancellationToken cancellationToken)
    {
        var balance = consumer.Wallet.Balance;

        string? message = null;
        NotificationEventType? eventType = null;

        if (!consumer.Wallet.IsWithinEmergencyCredit)
        {
            eventType = NotificationEventType.DisconnectionEligible;
            message = $"Prepaid balance for {consumer.AccountNumber} is Rs.{balance}. Emergency credit exhausted — supply is disconnection-eligible.";
        }
        else if (balance <= 0)
        {
            eventType = NotificationEventType.EmergencyCredit;
            message = $"Prepaid balance for {consumer.AccountNumber} is Rs.{balance}. Emergency credit is being used. Please recharge.";
        }
        else if (balance < LowBalanceThreshold)
        {
            eventType = NotificationEventType.LowBalance;
            message = $"Prepaid balance for {consumer.AccountNumber} is Rs.{balance}. Please recharge to avoid supply interruption.";
        }

        if (eventType is null || message is null)
            return;

        // One pending/unsent notification of this type per consumer at a time — a re-run of the
        // same day should not queue duplicate SMS events.
        var alreadyQueued = await _db.NotificationEvents.AnyAsync(
            n => n.ConsumerId == consumer.Id && n.EventType == eventType && n.Status == NotificationStatus.Pending,
            cancellationToken);
        if (alreadyQueued)
            return;

        _db.NotificationEvents.Add(new NotificationEvent(Guid.NewGuid(), consumer.Id, eventType.Value, message, DateTime.UtcNow));
    }

    private async Task RaiseProvisionalNotificationAsync(Consumer consumer, CancellationToken cancellationToken)
    {
        var alreadyQueued = await _db.NotificationEvents.AnyAsync(
            n => n.ConsumerId == consumer.Id && n.EventType == NotificationEventType.BillingProvisional && n.Status == NotificationStatus.Pending,
            cancellationToken);
        if (alreadyQueued)
            return;

        var message = $"Actual prepaid billing for {consumer.AccountNumber} is temporarily suspended due to a meter data validation exception. " +
                       "Provisional billing will continue until the exception is resolved.";
        _db.NotificationEvents.Add(new NotificationEvent(Guid.NewGuid(), consumer.Id, NotificationEventType.BillingProvisional, message, DateTime.UtcNow));
    }
}
