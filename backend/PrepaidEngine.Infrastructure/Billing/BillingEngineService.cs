using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.Billing;

/// <summary>
/// Implements <see cref="IBillingEngineService"/> directly against
/// <see cref="PrepaidEngineDbContext"/>. Daily Load Profile (DLP) is the sole driver of ongoing
/// prepaid billing, split across two daily stages by receipt time — see the interface's doc
/// comment for the full rule.
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
        var receivedAt = DateTime.UtcNow;

        var existing = await _db.DailyLoadProfiles.FirstOrDefaultAsync(
            d => d.ConsumerId == request.ConsumerId && d.MeterId == request.MeterId && d.ProfileDate == request.ProfileDate,
            cancellationToken);

        // Validate before accepting: this profile's starting reading must not be lower than the
        // same meter's previous day's closing reading — a negative-consumption sequence across
        // the day boundary, never silently billed. Mirrors the old Load Survey pipeline's own
        // negative-consumption guard, now applied to DLP (the sole remaining billing input).
        var previousDay = await _db.DailyLoadProfiles
            .Where(d => d.MeterId == request.MeterId && d.ProfileDate < request.ProfileDate
                && !d.IsProvisional && d.Status != DailyProfileStatus.Rejected)
            .OrderByDescending(d => d.ProfileDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (previousDay is not null && request.StartCumulativeKwh < previousDay.EndCumulativeKwh)
        {
            var reason = $"Negative consumption detected: this DLP's starting reading {request.StartCumulativeKwh} " +
                         $"is lower than the previous day's ({previousDay.ProfileDate}) closing reading {previousDay.EndCumulativeKwh}.";

            var control = await _db.MeterBillingControls.FirstOrDefaultAsync(
                c => c.MeterId == request.MeterId, cancellationToken);
            if (control is null)
            {
                control = new MeterBillingControl(Guid.NewGuid(), request.ConsumerId, request.MeterId, reason, DateTime.UtcNow);
                _db.MeterBillingControls.Add(control);
            }
            else if (!control.ActualBillingBlocked)
            {
                control.Reactivate(reason, DateTime.UtcNow);
            }

            if (existing is not null)
            {
                existing.MarkRejected();
                await _db.SaveChangesAsync(cancellationToken);
                return new DailyLoadProfileIngestResult(existing.Id, existing.Status.ToString(), false, reason);
            }

            DailyLoadProfile rejected;
            try
            {
                rejected = new DailyLoadProfile(
                    Guid.NewGuid(), request.ConsumerId, request.MeterId, request.ProfileDate, request.GeneratedAt,
                    request.StartCumulativeKwh, request.EndCumulativeKwh, receivedAt, isProvisional: false, request.SourceReference);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // Also internally inconsistent (end < start) — report that instead, it's the more
                // specific problem.
                return new DailyLoadProfileIngestResult(Guid.Empty, "Rejected", false, ex.Message);
            }

            rejected.MarkRejected();
            _db.DailyLoadProfiles.Add(rejected);
            await _db.SaveChangesAsync(cancellationToken);
            return new DailyLoadProfileIngestResult(rejected.Id, rejected.Status.ToString(), false, reason);
        }

        if (existing is not null)
        {
            if (!existing.IsProvisional)
            {
                return new DailyLoadProfileIngestResult(existing.Id, existing.Status.ToString(), false,
                    "A non-provisional DLP already exists for this consumer/meter/date.");
            }

            try
            {
                existing.ReplaceWithActual(request.StartCumulativeKwh, request.EndCumulativeKwh, request.GeneratedAt, receivedAt, request.SourceReference);
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
                request.StartCumulativeKwh, request.EndCumulativeKwh, receivedAt, isProvisional: false, request.SourceReference);
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

    public async Task<IReadOnlyList<DailyProcessingResult>> ProcessDailyStage1Async(
        DateOnly billingDate, DateTime stage1CutoffUtc, CancellationToken cancellationToken = default)
    {
        const string stage = "Stage1";

        var existingRun = await _db.BillingRuns.FirstOrDefaultAsync(
            r => r.RunType == BillingRun.DlpStage1RunType && r.BillingDate == billingDate, cancellationToken);
        if (existingRun is not null)
        {
            return new List<DailyProcessingResult>
            {
                new(Guid.Empty, stage, false, false, 0, true, $"Stage 1 already ran for {billingDate:yyyy-MM-dd} (status {existingRun.Status}).")
            };
        }

        var run = new BillingRun(Guid.NewGuid(), BillingRun.DlpStage1RunType, billingDate, DateTime.UtcNow);
        _db.BillingRuns.Add(run);

        var results = new List<DailyProcessingResult>();

        var consumers = await _db.Consumers
            .Include(c => c.Meter)
            .Include(c => c.Wallet).ThenInclude(w => w.Transactions)
            .Where(c => c.BillingMode == BillingMode.Prepaid && c.TariffId != null)
            .ToListAsync(cancellationToken);

        foreach (var consumer in consumers)
        {
            var dlp = await _db.DailyLoadProfiles.FirstOrDefaultAsync(
                d => d.ConsumerId == consumer.Id && d.MeterId == consumer.Meter.Id && d.ProfileDate == billingDate, cancellationToken);

            // Stage 1 only bills DLPs actually received by the cutoff — anything later (or still
            // missing) waits for Stage 2, which is also where a missing DLP gets its provisional
            // estimate, never here.
            if (dlp is null || dlp.IsProvisional || dlp.Status == DailyProfileStatus.Rejected || dlp.ReceivedAt > stage1CutoffUtc)
                continue;

            run.RecordConsumer();
            var result = await ChargeFromDlpAsync(consumer, dlp, stage, cancellationToken);
            if (result is not null)
                results.Add(result);
        }

        run.Complete(DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return results;
    }

    public async Task<IReadOnlyList<DailyProcessingResult>> ProcessDailyStage2Async(
        DateOnly billingDate, DateTime stage2CutoffUtc, CancellationToken cancellationToken = default)
    {
        const string stage = "Stage2";

        var existingRun = await _db.BillingRuns.FirstOrDefaultAsync(
            r => r.RunType == BillingRun.DlpStage2RunType && r.BillingDate == billingDate, cancellationToken);
        if (existingRun is not null)
        {
            return new List<DailyProcessingResult>
            {
                new(Guid.Empty, stage, false, false, 0, true, $"Stage 2 already ran for {billingDate:yyyy-MM-dd} (status {existingRun.Status}).")
            };
        }

        var run = new BillingRun(Guid.NewGuid(), BillingRun.DlpStage2RunType, billingDate, DateTime.UtcNow);
        _db.BillingRuns.Add(run);

        var results = new List<DailyProcessingResult>();

        var consumers = await _db.Consumers
            .Include(c => c.Meter)
            .Include(c => c.Wallet).ThenInclude(w => w.Transactions)
            .Where(c => c.BillingMode == BillingMode.Prepaid && c.TariffId != null)
            .ToListAsync(cancellationToken);

        foreach (var consumer in consumers)
        {
            var control = await _db.MeterBillingControls.FirstOrDefaultAsync(
                c => c.ConsumerId == consumer.Id && c.ActualBillingBlocked, cancellationToken);
            if (control is not null)
            {
                run.RecordConsumer();
                results.Add(new DailyProcessingResult(consumer.Id, stage, false, false, 0, true,
                    "Actual DLP billing is on hold for this meter."));
                continue;
            }

            var tariff = await _db.Tariffs.FirstOrDefaultAsync(t => t.Id == consumer.TariffId, cancellationToken);
            if (tariff is null)
            {
                run.RecordConsumer();
                run.RecordException();
                results.Add(new DailyProcessingResult(consumer.Id, stage, false, false, 0, true, "Assigned tariff could not be loaded."));
                continue;
            }

            var dlp = await _db.DailyLoadProfiles.FirstOrDefaultAsync(
                d => d.ConsumerId == consumer.Id && d.MeterId == consumer.Meter.Id && d.ProfileDate == billingDate, cancellationToken);

            if (dlp is not null && !dlp.IsProvisional && dlp.Status != DailyProfileStatus.Rejected && dlp.ReceivedAt <= stage2CutoffUtc)
            {
                // Arrived after Stage 1's cutoff but by Stage 2's — bill it now, for real.
                run.RecordConsumer();
                var result = await ChargeFromDlpAsync(consumer, dlp, stage, cancellationToken);
                if (result is not null)
                    results.Add(result);
                continue;
            }

            if (dlp is not null && !dlp.IsProvisional && dlp.Status != DailyProfileStatus.Rejected)
                continue; // arrived after the 12:00 PM cutoff itself — waits for a future stage, not billed here.

            if (dlp is not null && dlp.Status == DailyProfileStatus.Rejected)
            {
                // A Rejected DLP already occupies this Consumer+Meter+ProfileDate slot (its
                // MeterBillingControl hold may since have been cleared without a corrected DLP
                // re-ingest yet) — DailyLoadProfile has a unique index on that triple, so a
                // provisional profile can never be created here without colliding with it.
                // Waits for a real corrected re-ingest, never silently double-billed or crashed.
                run.RecordConsumer();
                results.Add(new DailyProcessingResult(consumer.Id, stage, false, false, 0, true,
                    "The DLP for this date was rejected and has not been re-ingested with corrected data."));
                continue;
            }

            // Still no usable DLP by the 12:00 PM cutoff: provisional charge estimated from
            // recent history, never treating the missing day as zero consumption without saying so.
            run.RecordConsumer();

            var recentValid = await _db.DailyLoadProfiles
                .Where(d => d.ConsumerId == consumer.Id && d.MeterId == consumer.Meter.Id && !d.IsProvisional && d.Status != DailyProfileStatus.Rejected)
                .OrderByDescending(d => d.ProfileDate)
                .Take(ProvisionalEstimationWindow)
                .ToListAsync(cancellationToken);
            var estimatedKwh = recentValid.Count > 0 ? recentValid.Average(d => d.TotalKwh) : 0m;

            var provisional = DailyLoadProfile.CreateProvisional(Guid.NewGuid(), consumer.Id, consumer.Meter.Id, billingDate, DateTime.UtcNow, estimatedKwh);
            _db.DailyLoadProfiles.Add(provisional);

            var chargeAmount = CalculateDailyCharge(tariff, consumer, estimatedKwh);
            var reference = $"DLP-PROV:{provisional.Id}";
            if (chargeAmount > 0)
            {
                var debit = consumer.Wallet.Debit(chargeAmount, WalletTransactionType.DailyDlpCharge, reference);
                _db.WalletTransactions.Add(debit);
            }

            await RaiseProvisionalNotificationAsync(consumer, cancellationToken);
            await RaiseCreditNotificationsAsync(consumer, cancellationToken);
            await _emergencyCreditGuard.EvaluateAsync(consumer, cancellationToken);

            results.Add(new DailyProcessingResult(consumer.Id, "Stage2Provisional", false, true, chargeAmount, false, null));
        }

        run.Complete(DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return results;
    }

    /// <summary>Shared real-DLP charging logic for both stages — posts one direct debit from the
    /// DLP's own total kWh, idempotent on the DLP's own reference.</summary>
    private async Task<DailyProcessingResult?> ChargeFromDlpAsync(
        Consumer consumer, DailyLoadProfile dlp, string stage, CancellationToken cancellationToken)
    {
        if (dlp.TotalKwh < 0)
        {
            return new DailyProcessingResult(consumer.Id, stage, true, false, 0, true, "DLP has negative total consumption.");
        }

        var tariff = await _db.Tariffs.FirstOrDefaultAsync(t => t.Id == consumer.TariffId, cancellationToken);
        if (tariff is null)
        {
            return new DailyProcessingResult(consumer.Id, stage, true, false, 0, true, "Assigned tariff could not be loaded.");
        }

        var reference = $"DLP:{dlp.Id}";
        var alreadyBilled = consumer.Wallet.Transactions.Any(t => t.Reference == reference);
        if (alreadyBilled || dlp.Status == DailyProfileStatus.Billed)
        {
            return new DailyProcessingResult(consumer.Id, stage, true, false, 0, true, "This DLP has already been billed.");
        }

        var chargeAmount = CalculateDailyCharge(tariff, consumer, dlp.TotalKwh);
        if (chargeAmount > 0)
        {
            var debit = consumer.Wallet.Debit(chargeAmount, WalletTransactionType.DailyDlpCharge, reference);
            _db.WalletTransactions.Add(debit);
        }

        dlp.MarkBilled();
        await RaiseCreditNotificationsAsync(consumer, cancellationToken);
        await _emergencyCreditGuard.EvaluateAsync(consumer, cancellationToken);

        return new DailyProcessingResult(consumer.Id, stage, true, false, chargeAmount, false, null);
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
