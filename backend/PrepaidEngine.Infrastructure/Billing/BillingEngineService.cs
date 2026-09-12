using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.Billing;

/// <summary>
/// Implements <see cref="IBillingEngineService"/> directly against
/// <see cref="PrepaidEngineDbContext"/>. Intentionally one combined service at this stage (per
/// the spec's own §55: production should eventually split this into
/// MeterDataIngestionService/LoadSurveyVeeService/HourlyPrepaidBillingService/
/// DailyDlpBillingService/ProvisionalBillingService/MeterReplacementService/NotificationService)
/// — kept combined here so the initial LS/DLP change is understandable and deployable without
/// adding infrastructure this demo stage doesn't need.
/// </summary>
public class BillingEngineService : IBillingEngineService
{
    /// <summary>Demo low-balance notification threshold (spec §19). Production values must be
    /// configuration, not a constant — documented as a known follow-up.</summary>
    private const decimal LowBalanceThreshold = 100m;

    /// <summary>Demo provisional-DLP estimation window (spec §13.1): average of up to this many
    /// previous valid DLP records for the consumer/meter.</summary>
    private const int ProvisionalEstimationWindow = 7;

    private readonly PrepaidEngineDbContext _db;

    public BillingEngineService(PrepaidEngineDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<LoadSurveyIngestResult>> IngestLoadSurveyAsync(
        IReadOnlyList<LoadSurveyBlockRequest> blocks, CancellationToken cancellationToken = default)
    {
        var results = new List<LoadSurveyIngestResult>();

        foreach (var block in blocks)
        {
            // Duplicate protection: MeterId + IntervalStart + IntervalEnd uniqueness (spec §5.2)
            // is also enforced at the database level (see LoadSurveyIntervalConfiguration) as the
            // final safety barrier under concurrent ingestion — this check gives a clean, honest
            // per-block result rather than surfacing a raw constraint-violation exception.
            var duplicate = await _db.LoadSurveyIntervals.AnyAsync(
                l => l.MeterId == block.MeterId && l.IntervalStart == block.IntervalStart && l.IntervalEnd == block.IntervalEnd,
                cancellationToken);

            LoadSurveyInterval interval;
            try
            {
                interval = new LoadSurveyInterval(
                    Guid.NewGuid(), block.ConsumerId, block.MeterId, block.IntervalStart, block.IntervalEnd,
                    block.CumulativeKwh, block.IntervalKwh, DateTime.UtcNow, block.SourceReference);
            }
            catch (ArgumentException ex)
            {
                results.Add(new LoadSurveyIngestResult(Guid.Empty, "Invalid", "Rejected", ex.Message));
                continue;
            }

            if (duplicate)
            {
                // Never persisted: the whole point of the MeterId+IntervalStart+IntervalEnd
                // unique index (spec §5.2) is that exactly one row exists for this natural key —
                // the original block is already stored, so this one is reported rejected without
                // being added to the DbContext at all.
                interval.MarkRejected(LoadSurveyQuality.Duplicate);
                results.Add(new LoadSurveyIngestResult(interval.Id, interval.Quality.ToString(), interval.Status.ToString(),
                    "Duplicate block for this meter/interval — ignored."));
                continue;
            }

            // Sequence continuity: compare against the immediately preceding block for the SAME
            // physical meter only (MeterId scopes this naturally — see MeterAssignment's doc
            // comment on why a meter replacement can never produce a false-positive here).
            var previous = await _db.LoadSurveyIntervals
                .Where(l => l.MeterId == block.MeterId && l.IntervalEnd <= block.IntervalStart)
                .OrderByDescending(l => l.IntervalEnd)
                .FirstOrDefaultAsync(cancellationToken);

            if (previous is not null && block.CumulativeKwh < previous.CumulativeKwh)
            {
                // Negative consumption is a data-quality event, never silently zeroed (spec §7).
                interval.MarkRejected(LoadSurveyQuality.NegativeConsumption);
                _db.LoadSurveyIntervals.Add(interval);

                var control = await _db.MeterBillingControls.FirstOrDefaultAsync(
                    c => c.MeterId == block.MeterId, cancellationToken);
                var reason = $"Negative consumption detected: cumulative {block.CumulativeKwh} < previous {previous.CumulativeKwh}.";
                if (control is null)
                {
                    control = new MeterBillingControl(Guid.NewGuid(), block.ConsumerId, block.MeterId, reason, DateTime.UtcNow);
                    _db.MeterBillingControls.Add(control);
                }
                else if (!control.ActualBillingBlocked)
                {
                    control.Reactivate(reason, DateTime.UtcNow);
                }

                results.Add(new LoadSurveyIngestResult(interval.Id, interval.Quality.ToString(), interval.Status.ToString(), reason));
                continue;
            }

            interval.MarkValidated();
            _db.LoadSurveyIntervals.Add(interval);
            results.Add(new LoadSurveyIngestResult(interval.Id, interval.Quality.ToString(), interval.Status.ToString(), null));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return results;
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

    public async Task<IReadOnlyList<HourlyProcessingResult>> ProcessCompletedHourAsync(
        DateTime hourEndUtc, CancellationToken cancellationToken = default)
    {
        var hourStart = hourEndUtc.AddHours(-1);
        var results = new List<HourlyProcessingResult>();

        var blocksInHour = await _db.LoadSurveyIntervals
            .Where(l => l.IntervalStart >= hourStart && l.IntervalEnd <= hourEndUtc && l.Status == LoadSurveyStatus.Validated)
            .ToListAsync(cancellationToken);

        foreach (var consumerGroup in blocksInHour.GroupBy(b => b.ConsumerId))
        {
            var consumerId = consumerGroup.Key;
            var consumer = await _db.Consumers.Include(c => c.Wallet).ThenInclude(w => w.Transactions)
                .FirstOrDefaultAsync(c => c.Id == consumerId, cancellationToken);
            if (consumer is null)
            {
                results.Add(new HourlyProcessingResult(consumerId, 0, 0m, true, "Consumer not found."));
                continue;
            }

            var control = await _db.MeterBillingControls.FirstOrDefaultAsync(
                c => c.ConsumerId == consumerId && c.ActualBillingBlocked, cancellationToken);
            if (control is not null)
            {
                results.Add(new HourlyProcessingResult(consumerId, 0, 0m, true, "Actual billing is on hold for this meter (negative-consumption exception)."));
                continue;
            }

            if (consumer.TariffId is null)
            {
                results.Add(new HourlyProcessingResult(consumerId, 0, 0m, true, "Consumer has no assigned tariff."));
                continue;
            }

            var tariff = await _db.Tariffs.Include(t => t.Slabs).FirstOrDefaultAsync(t => t.Id == consumer.TariffId, cancellationToken);
            if (tariff is null)
            {
                results.Add(new HourlyProcessingResult(consumerId, 0, 0m, true, "Assigned tariff could not be loaded."));
                continue;
            }

            decimal totalCharge = 0m;
            var processedCount = 0;

            foreach (var block in consumerGroup)
            {
                var reference = $"LS:{block.Id}";
                // Idempotency: the reference is also a unique index at the database level (spec
                // §10) — this check keeps a re-run of the same hour a clean no-op rather than a
                // constraint-violation exception.
                var alreadyPosted = consumer.Wallet.Transactions.Any(t => t.Reference == reference);
                if (alreadyPosted || block.Status == LoadSurveyStatus.Processed)
                    continue;

                var grossEnergyCharge = tariff.CalculateEnergyCharge(block.IntervalKwh);
                var rebate = grossEnergyCharge * (tariff.PrepaidEnergyRebatePercent / 100m);
                var netCharge = Math.Round(grossEnergyCharge - rebate, 2, MidpointRounding.AwayFromZero);

                if (netCharge > 0)
                {
                    var walletTransaction = consumer.Wallet.Debit(netCharge, WalletTransactionType.LoadSurveyHourlyCharge, reference);
                    _db.WalletTransactions.Add(walletTransaction);
                    totalCharge += netCharge;
                }

                block.MarkProcessed();
                processedCount++;
            }

            if (processedCount > 0)
                await RaiseCreditNotificationsAsync(consumer, cancellationToken);

            results.Add(new HourlyProcessingResult(consumerId, processedCount, totalCharge, false, null));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return results;
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
                new(Guid.Empty, false, false, 0, 0, 0, 0, true, $"A daily billing run already exists for {billingDate:yyyy-MM-dd} (status {existingRun.Status}).")
            };
        }

        var run = new BillingRun(Guid.NewGuid(), BillingRun.DlpDailyRunType, billingDate, DateTime.UtcNow);
        _db.BillingRuns.Add(run);

        var results = new List<DailyProcessingResult>();
        var dayStart = billingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

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
                results.Add(new DailyProcessingResult(consumer.Id, false, false, 0, 0, 0, 0, true,
                    "Actual DLP billing is on hold for this meter (negative-consumption exception)."));
                continue;
            }

            var tariff = await _db.Tariffs.FirstOrDefaultAsync(t => t.Id == consumer.TariffId, cancellationToken);
            if (tariff is null)
            {
                run.RecordException();
                results.Add(new DailyProcessingResult(consumer.Id, false, false, 0, 0, 0, 0, true, "Assigned tariff could not be loaded."));
                continue;
            }

            var dlp = await _db.DailyLoadProfiles.FirstOrDefaultAsync(
                d => d.ConsumerId == consumer.Id && d.MeterId == consumer.Meter.Id && d.ProfileDate == billingDate, cancellationToken);

            var dlpAvailable = dlp is not null && !dlp.IsProvisional;
            decimal authoritativeCharge;

            if (dlp is null)
            {
                // Missing DLP: create a provisional profile estimated from recent valid history
                // (spec §13.1) rather than treating the missing day as zero consumption.
                var recentValid = await _db.DailyLoadProfiles
                    .Where(d => d.ConsumerId == consumer.Id && d.MeterId == consumer.Meter.Id && !d.IsProvisional)
                    .OrderByDescending(d => d.ProfileDate)
                    .Take(ProvisionalEstimationWindow)
                    .ToListAsync(cancellationToken);
                var estimatedKwh = recentValid.Count > 0 ? recentValid.Average(d => d.TotalKwh) : 0m;

                dlp = DailyLoadProfile.CreateProvisional(Guid.NewGuid(), consumer.Id, consumer.Meter.Id, billingDate, DateTime.UtcNow, estimatedKwh);
                _db.DailyLoadProfiles.Add(dlp);

                authoritativeCharge = CalculateDailyCharge(tariff, consumer, estimatedKwh);
                var provisionalReference = $"DLP-PROV:{dlp.Id}";
                if (authoritativeCharge > 0)
                {
                    var provisionalTransaction = consumer.Wallet.Debit(authoritativeCharge, WalletTransactionType.DlpSettlementAdjustment, provisionalReference);
                    _db.WalletTransactions.Add(provisionalTransaction);
                }

                await RaiseProvisionalNotificationAsync(consumer, cancellationToken);
                await RaiseCreditNotificationsAsync(consumer, cancellationToken);

                results.Add(new DailyProcessingResult(consumer.Id, false, true, authoritativeCharge, 0, authoritativeCharge, 0, false, null));
                continue;
            }

            if (dlp.TotalKwh < 0)
            {
                run.RecordException();
                results.Add(new DailyProcessingResult(consumer.Id, true, dlp.IsProvisional, 0, 0, 0, 0, true, "DLP has negative total consumption."));
                continue;
            }

            authoritativeCharge = CalculateDailyCharge(tariff, consumer, dlp.TotalKwh);

            // Identify the day's LS debits by which LoadSurveyInterval they came from (matched by
            // the interval's own IntervalStart date), not by the wallet transaction's OccurredAt
            // — hourly processing can run at any wall-clock time relative to the interval it's
            // processing (e.g. a few minutes after the hour, or backdated/late-arriving data), so
            // OccurredAt is never a reliable proxy for "which calendar day this consumption
            // belongs to".
            var dayIntervalReferences = await _db.LoadSurveyIntervals
                .Where(l => l.ConsumerId == consumer.Id && l.MeterId == consumer.Meter.Id
                    && l.IntervalStart >= dayStart && l.IntervalStart < dayEnd && l.Status == LoadSurveyStatus.Processed)
                .Select(l => $"LS:{l.Id}")
                .ToListAsync(cancellationToken);
            var dayIntervalReferenceSet = dayIntervalReferences.ToHashSet();

            var lsDebitTotal = consumer.Wallet.Transactions
                .Where(t => t.Type == WalletTransactionType.LoadSurveyHourlyCharge
                    && t.Reference is not null && dayIntervalReferenceSet.Contains(t.Reference))
                .Sum(t => -t.Amount);

            var provisionalDebit = consumer.Wallet.Transactions
                .Where(t => t.Reference == $"DLP-PROV:{dlp.Id}")
                .Sum(t => -t.Amount);

            var settlement = Math.Round(authoritativeCharge - lsDebitTotal - provisionalDebit, 2, MidpointRounding.AwayFromZero);
            var settleReference = $"DLP-SETTLE:{dlp.Id}";

            if (settlement > 0)
            {
                var debit = consumer.Wallet.Debit(settlement, WalletTransactionType.DlpSettlementAdjustment, settleReference);
                _db.WalletTransactions.Add(debit);
            }
            else if (settlement < 0)
            {
                var credit = consumer.Wallet.Credit(-settlement, WalletTransactionType.DlpSettlementAdjustment, settleReference);
                _db.WalletTransactions.Add(credit);
            }
            // settlement == 0: no financial transaction, per spec §4.3.

            dlp.MarkBilled();
            await RaiseCreditNotificationsAsync(consumer, cancellationToken);

            results.Add(new DailyProcessingResult(consumer.Id, dlpAvailable, dlp.IsProvisional, authoritativeCharge, lsDebitTotal, provisionalDebit, settlement, false, null));
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
        // same hour/day should not queue duplicate SMS events.
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
