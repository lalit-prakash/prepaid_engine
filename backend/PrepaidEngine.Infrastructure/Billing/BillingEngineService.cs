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

    /// <summary>Demo provisional-DLP estimation window: average of up to this many previous
    /// valid DLP records for the consumer/meter.</summary>
    private const int ProvisionalEstimationWindow = 7;

    private readonly PrepaidEngineDbContext _db;
    private readonly IEmergencyCreditGuard _emergencyCreditGuard;

    private readonly decimal _lowBalanceThreshold;

    /// <param name="lowBalance">Optional; the low-balance warning threshold comes from the "LowBalance" settings (default Rs.100).</param>
    public BillingEngineService(
        PrepaidEngineDbContext db, IEmergencyCreditGuard emergencyCreditGuard,
        Microsoft.Extensions.Options.IOptions<PrepaidEngine.Application.Wallets.LowBalanceOptions>? lowBalance = null)
    {
        _db = db;
        _emergencyCreditGuard = emergencyCreditGuard;
        _lowBalanceThreshold = lowBalance?.Value.NotificationThreshold ?? PrepaidEngine.Application.Wallets.LowBalanceOptions.DefaultNotificationThreshold;
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

    /// <summary>How long a running billing run may go without a heartbeat before another instance may take it over.</summary>
    private static readonly TimeSpan RunLease = TimeSpan.FromMinutes(10);

    /// <summary>Consumers processed and committed together. Keeps memory flat and makes each commit small.</summary>
    private const int BatchSize = 500;

    /// <summary>Most per-consumer results handed back to the caller; the run row always holds the true counts.</summary>
    private const int MaxResultsReturned = 5000;

    public Task<IReadOnlyList<DailyProcessingResult>> ProcessDailyStage1Async(
        DateOnly billingDate, DateTime stage1CutoffUtc, CancellationToken cancellationToken = default)
        => RunStageAsync(BillingRun.DlpStage1RunType, "Stage1", billingDate, stage1CutoffUtc, isStage2: false, cancellationToken);

    public Task<IReadOnlyList<DailyProcessingResult>> ProcessDailyStage2Async(
        DateOnly billingDate, DateTime stage2CutoffUtc, CancellationToken cancellationToken = default)
        => RunStageAsync(BillingRun.DlpStage2RunType, "Stage2", billingDate, stage2CutoffUtc, isStage2: true, cancellationToken);

    /// <summary>
    /// Runs one billing stage for one date. The run row is the claim: the unique (RunType, BillingDate)
    /// index lets exactly one instance create it, and a run that stops heartbeating for <see cref="RunLease"/>
    /// can be taken over and resumed from its saved cursor. Consumers are processed in fixed-size batches in
    /// key order; each batch (charges, notifications and the run's progress and cursor) is one transaction, so a
    /// crash loses at most one batch and never double-charges (debits are also idempotent on the DLP reference).
    /// </summary>
    private async Task<IReadOnlyList<DailyProcessingResult>> RunStageAsync(
        string runType, string stage, DateOnly billingDate, DateTime cutoffUtc, bool isStage2, CancellationToken cancellationToken)
    {
        var stageLabel = isStage2 ? "Stage 2" : "Stage 1";
        var claim = await ClaimRunAsync(runType, billingDate, cancellationToken);
        if (claim.Message is not null)
            return new List<DailyProcessingResult> { new(Guid.Empty, stage, false, false, 0, true, claim.Message.Replace("{stage}", stageLabel)) };

        var runId = claim.RunId;
        var cursor = claim.ResumeAfter;
        var results = new List<DailyProcessingResult>();

        while (true)
        {
            var batch = await _db.Consumers
                .Include(c => c.Meter)
                .Include(c => c.Wallet)
                .Where(c => c.BillingMode == BillingMode.Prepaid && c.TariffId != null && (cursor == null || c.Id.CompareTo(cursor.Value) > 0))
                .OrderBy(c => c.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
                break;

            var consumerIds = batch.Select(c => c.Id).ToList();
            var dlps = (await _db.DailyLoadProfiles
                    .Where(d => d.ProfileDate == billingDate && consumerIds.Contains(d.ConsumerId))
                    .ToListAsync(cancellationToken))
                .GroupBy(d => (d.ConsumerId, d.MeterId))
                .ToDictionary(g => g.Key, g => g.First());
            var tariffIds = batch.Select(c => c.TariffId!.Value).Distinct().ToList();
            var tariffs = await _db.Tariffs.Where(t => tariffIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
            var billedRefs = await LoadBilledReferencesAsync(batch, dlps, cancellationToken);
            var heldConsumers = isStage2
                ? (await _db.MeterBillingControls.Where(c => consumerIds.Contains(c.ConsumerId) && c.ActualBillingBlocked)
                    .Select(c => c.ConsumerId).ToListAsync(cancellationToken)).ToHashSet()
                : new HashSet<Guid>();

            int processed = 0, exceptions = 0;
            foreach (var consumer in batch)
            {
                dlps.TryGetValue((consumer.Id, consumer.Meter.Id), out var dlp);
                tariffs.TryGetValue(consumer.TariffId!.Value, out var tariff);

                if (!isStage2)
                {
                    // Stage 1 only bills DLPs actually received by the cutoff; anything later (or still missing)
                    // waits for Stage 2, which is also where a missing DLP gets its provisional estimate, never here.
                    if (dlp is null || dlp.IsProvisional || dlp.Status == DailyProfileStatus.Rejected || dlp.ReceivedAt > cutoffUtc)
                        continue;

                    processed++;
                    if (await ChargeFromDlpAsync(consumer, dlp, tariff, billedRefs, stage, cancellationToken) is { } r1)
                        AddResult(results, r1);
                    continue;
                }

                if (heldConsumers.Contains(consumer.Id))
                {
                    processed++;
                    AddResult(results, new DailyProcessingResult(consumer.Id, stage, false, false, 0, true, "Actual DLP billing is on hold for this meter."));
                    continue;
                }

                if (tariff is null)
                {
                    processed++;
                    exceptions++;
                    AddResult(results, new DailyProcessingResult(consumer.Id, stage, false, false, 0, true, "Assigned tariff could not be loaded."));
                    continue;
                }

                if (dlp is not null && !dlp.IsProvisional && dlp.Status != DailyProfileStatus.Rejected && dlp.ReceivedAt <= cutoffUtc)
                {
                    // Arrived after Stage 1's cutoff but by Stage 2's: bill it now, for real.
                    processed++;
                    if (await ChargeFromDlpAsync(consumer, dlp, tariff, billedRefs, stage, cancellationToken) is { } r2)
                        AddResult(results, r2);
                    continue;
                }

                if (dlp is not null && !dlp.IsProvisional && dlp.Status != DailyProfileStatus.Rejected)
                    continue; // arrived after the 12:00 PM cutoff itself: waits for a future stage, not billed here.

                if (dlp is not null && dlp.Status == DailyProfileStatus.Rejected)
                {
                    // A Rejected DLP already occupies this Consumer+Meter+ProfileDate slot (its hold may since have been
                    // cleared without a corrected re-ingest); a provisional profile would collide with the unique index.
                    processed++;
                    AddResult(results, new DailyProcessingResult(consumer.Id, stage, false, false, 0, true,
                        "The DLP for this date was rejected and has not been re-ingested with corrected data."));
                    continue;
                }

                if (dlp is not null && dlp.IsProvisional)
                    continue; // already provisionally billed for this date

                // Still no usable DLP by the 12:00 PM cutoff: provisional charge estimated from recent history,
                // never treating the missing day as zero consumption without saying so.
                processed++;
                var recentValid = await _db.DailyLoadProfiles
                    .Where(d => d.ConsumerId == consumer.Id && d.MeterId == consumer.Meter.Id && !d.IsProvisional && d.Status != DailyProfileStatus.Rejected)
                    .OrderByDescending(d => d.ProfileDate)
                    .Take(ProvisionalEstimationWindow)
                    .ToListAsync(cancellationToken);
                var estimatedKwh = recentValid.Count > 0 ? recentValid.Average(d => d.TotalKwh) : 0m;

                var provisional = DailyLoadProfile.CreateProvisional(Guid.NewGuid(), consumer.Id, consumer.Meter.Id, billingDate, DateTime.UtcNow, estimatedKwh);
                _db.DailyLoadProfiles.Add(provisional);

                var chargeAmount = CalculateDailyCharge(tariff, consumer, estimatedKwh);
                if (chargeAmount > 0)
                {
                    var debit = consumer.Wallet.Debit(chargeAmount, WalletTransactionType.DailyDlpCharge, $"DLP-PROV:{provisional.Id}");
                    _db.WalletTransactions.Add(debit);
                }

                await RaiseProvisionalNotificationAsync(consumer, cancellationToken);
                await RaiseCreditNotificationsAsync(consumer, cancellationToken);
                await _emergencyCreditGuard.EvaluateAsync(consumer, cancellationToken);

                AddResult(results, new DailyProcessingResult(consumer.Id, "Stage2Provisional", false, true, chargeAmount, false, null));
            }

            // One transaction per batch: the charges above plus the run's progress, cursor and heartbeat.
            var run = await _db.BillingRuns.FirstAsync(r => r.Id == runId, cancellationToken);
            run.RecordBatch(processed, exceptions, batch[^1].Id, DateTime.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            cursor = batch[^1].Id;
        }

        var finished = await _db.BillingRuns.FirstAsync(r => r.Id == runId, cancellationToken);
        finished.Complete(DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        return results;
    }

    private static void AddResult(List<DailyProcessingResult> results, DailyProcessingResult result)
    {
        if (results.Count < MaxResultsReturned)
            results.Add(result);
    }

    private sealed record RunClaim(Guid RunId, Guid? ResumeAfter, string? Message);

    /// <summary>
    /// Claims the run for this instance: creates it, resumes one whose owner stopped heartbeating, or refuses
    /// because it is running elsewhere or already finished. The unique index and a conditional update decide
    /// races, so two instances can never work the same run at once.
    /// </summary>
    private async Task<RunClaim> ClaimRunAsync(string runType, DateOnly billingDate, CancellationToken cancellationToken)
    {
        var existing = await _db.BillingRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunType == runType && r.BillingDate == billingDate, cancellationToken);

        if (existing is null)
        {
            var run = new BillingRun(Guid.NewGuid(), runType, billingDate, DateTime.UtcNow);
            _db.BillingRuns.Add(run);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                return new RunClaim(run.Id, null, null);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear(); // another instance created it first
                return new RunClaim(Guid.Empty, null, "{stage} is already running for " + billingDate.ToString("yyyy-MM-dd") + " on another instance.");
            }
        }

        if (existing.Status != BillingRunStatus.Running)
            return new RunClaim(Guid.Empty, null, $"{{stage}} already ran for {billingDate:yyyy-MM-dd} (status {existing.Status}).");

        var now = DateTime.UtcNow;
        var stale = now - RunLease;
        var won = await _db.BillingRuns
            .Where(r => r.Id == existing.Id && r.Status == BillingRunStatus.Running && r.LastHeartbeatAt < stale)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastHeartbeatAt, now), cancellationToken);
        return won == 1
            ? new RunClaim(existing.Id, existing.ResumeAfterConsumerId, null)
            : new RunClaim(Guid.Empty, null, "{stage} is already running for " + billingDate.ToString("yyyy-MM-dd") + " on another instance.");
    }

    /// <summary>The DLP debit references already posted for this batch's wallets, so a re-run never bills a DLP twice.</summary>
    private async Task<HashSet<string>> LoadBilledReferencesAsync(
        List<Consumer> batch, Dictionary<(Guid ConsumerId, Guid MeterId), DailyLoadProfile> dlps, CancellationToken cancellationToken)
    {
        var references = dlps.Values.Select(d => $"DLP:{d.Id}").ToList();
        if (references.Count == 0)
            return new HashSet<string>();
        var walletIds = batch.Select(c => c.Wallet.Id).ToList();
        var found = await _db.WalletTransactions
            .Where(t => walletIds.Contains(t.WalletId) && t.Reference != null && references.Contains(t.Reference))
            .Select(t => t.Reference!)
            .ToListAsync(cancellationToken);
        return found.ToHashSet();
    }

    /// <summary>Shared real-DLP charging logic for both stages: posts one direct debit from the DLP's own
    /// total kWh, idempotent on the DLP's own reference.</summary>
    private async Task<DailyProcessingResult?> ChargeFromDlpAsync(
        Consumer consumer, DailyLoadProfile dlp, Tariff? tariff, HashSet<string> billedRefs, string stage, CancellationToken cancellationToken)
    {
        if (dlp.TotalKwh < 0)
            return new DailyProcessingResult(consumer.Id, stage, true, false, 0, true, "DLP has negative total consumption.");

        if (tariff is null)
            return new DailyProcessingResult(consumer.Id, stage, true, false, 0, true, "Assigned tariff could not be loaded.");

        var reference = $"DLP:{dlp.Id}";
        if (billedRefs.Contains(reference) || dlp.Status == DailyProfileStatus.Billed)
            return new DailyProcessingResult(consumer.Id, stage, true, false, 0, true, "This DLP has already been billed.");

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
        else if (balance < _lowBalanceThreshold)
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
