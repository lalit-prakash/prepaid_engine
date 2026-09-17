using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrepaidEngine.Application.MeterData;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.MeterData;

/// <summary>See <see cref="IMeterDataIngestionService"/>.</summary>
public class MeterDataIngestionService : IMeterDataIngestionService
{
    private readonly PrepaidEngineDbContext _db;
    private readonly EnergyValidationOptions _options;

    public MeterDataIngestionService(PrepaidEngineDbContext db, IOptions<EnergyValidationOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<RegisterReadingIngestResult> IngestRegisterReadingAsync(RegisterReadingRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _db.RegisterReadings.FirstOrDefaultAsync(
            r => r.ConsumerId == request.ConsumerId && r.MeterId == request.MeterId && r.ReadingTimestamp == request.ReadingTimestamp,
            cancellationToken);
        if (existing is not null)
            return new RegisterReadingIngestResult(existing.Id, "Duplicate", "A BP register reading already exists for this consumer/meter/timestamp — ingest ignored.");

        RegisterReading reading;
        try
        {
            reading = new RegisterReading(
                Guid.NewGuid(), request.ConsumerId, request.MeterId, request.ReadingTimestamp,
                request.CumulativeImportKwh, DateTime.UtcNow, request.SourceReference);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new RegisterReadingIngestResult(Guid.Empty, "Rejected", ex.Message);
        }

        _db.RegisterReadings.Add(reading);
        await _db.SaveChangesAsync(cancellationToken);
        return new RegisterReadingIngestResult(reading.Id, reading.Status.ToString(), null);
    }

    public async Task<LoadSurveyIntervalIngestResult> IngestLoadSurveyIntervalAsync(LoadSurveyIntervalRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _db.LoadSurveyIntervals.FirstOrDefaultAsync(
            l => l.ConsumerId == request.ConsumerId && l.MeterId == request.MeterId && l.IntervalStart == request.IntervalStart,
            cancellationToken);
        if (existing is not null)
            return new LoadSurveyIntervalIngestResult(existing.Id, "Duplicate", "An LS interval already exists for this consumer/meter/interval-start — ingest ignored.");

        LoadSurveyInterval interval;
        try
        {
            interval = new LoadSurveyInterval(
                Guid.NewGuid(), request.ConsumerId, request.MeterId, request.IntervalStart, request.IntervalEnd,
                request.ImportKwh, DateTime.UtcNow, request.SourceReference);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new LoadSurveyIntervalIngestResult(Guid.Empty, "Rejected", ex.Message);
        }

        _db.LoadSurveyIntervals.Add(interval);
        await _db.SaveChangesAsync(cancellationToken);
        return new LoadSurveyIntervalIngestResult(interval.Id, "Received", null);
    }

    public async Task<InstantaneousReadingIngestResult> IngestInstantaneousReadingAsync(InstantaneousReadingRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _db.InstantaneousReadings.FirstOrDefaultAsync(
            i => i.MeterId == request.MeterId && i.Timestamp == request.Timestamp, cancellationToken);
        if (existing is not null)
            return new InstantaneousReadingIngestResult(existing.Id, "Duplicate", "An IP reading already exists for this meter/timestamp — ingest ignored.");

        InstantaneousReading reading;
        try
        {
            reading = new InstantaneousReading(
                Guid.NewGuid(), request.ConsumerId, request.MeterId, request.Timestamp, request.VoltageVolts,
                request.CurrentAmps, request.PowerKw, request.PowerFactor, request.FrequencyHz, request.RelayStatus,
                DateTime.UtcNow, request.SourceReference);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new InstantaneousReadingIngestResult(Guid.Empty, "Rejected", ex.Message);
        }

        _db.InstantaneousReadings.Add(reading);
        await _db.SaveChangesAsync(cancellationToken);
        return new InstantaneousReadingIngestResult(reading.Id, "Received", null);
    }

    public async Task<MeterEventIngestResult> IngestMeterEventAsync(MeterEventRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _db.MeterEvents.FirstOrDefaultAsync(
            e => e.MeterId == request.MeterId && e.EventCode == request.EventCode && e.EventTimestamp == request.EventTimestamp,
            cancellationToken);
        if (existing is not null)
            return new MeterEventIngestResult(existing.Id, "Duplicate", "This meter event already exists — ingest ignored.");

        var meterEvent = new MeterEvent(
            Guid.NewGuid(), request.ConsumerId, request.MeterId, request.EventCode, request.EventTimestamp,
            DateTime.UtcNow, request.Description, request.SourceReference);

        _db.MeterEvents.Add(meterEvent);
        await _db.SaveChangesAsync(cancellationToken);
        return new MeterEventIngestResult(meterEvent.Id, "Received", null);
    }

    public async Task<MeterAlarmIngestResult> IngestMeterAlarmAsync(MeterAlarmRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _db.MeterAlarms.FirstOrDefaultAsync(
            a => a.MeterId == request.MeterId && a.AlarmCode == request.AlarmCode && a.RaisedAt == request.RaisedAt,
            cancellationToken);
        if (existing is not null)
            return new MeterAlarmIngestResult(existing.Id, "Duplicate", "This meter alarm already exists — ingest ignored.");

        var alarm = new MeterAlarm(
            Guid.NewGuid(), request.ConsumerId, request.MeterId, request.AlarmCode, request.Severity,
            request.RaisedAt, DateTime.UtcNow, request.SourceReference);

        _db.MeterAlarms.Add(alarm);
        await _db.SaveChangesAsync(cancellationToken);
        return new MeterAlarmIngestResult(alarm.Id, "Open", null);
    }

    public async Task<IReadOnlyList<DlpCompletenessRow>> GetDlpCompletenessAsync(DateOnly profileDate, CancellationToken cancellationToken = default)
    {
        var activeConsumers = await _db.Consumers
            .Include(c => c.Meter)
            .Where(c => c.BillingMode == BillingMode.Prepaid)
            .Select(c => new { c.Id, c.AccountNumber, MeterId = c.Meter.Id, c.Meter.MeterNumber })
            .ToListAsync(cancellationToken);

        var dlpForDate = await _db.DailyLoadProfiles
            .Where(d => d.ProfileDate == profileDate)
            .ToListAsync(cancellationToken);
        var dlpByMeter = dlpForDate
            .GroupBy(d => d.MeterId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<DlpCompletenessRow>();
        foreach (var consumer in activeConsumers)
        {
            if (!dlpByMeter.TryGetValue(consumer.MeterId, out var profiles) || profiles.Count == 0)
            {
                rows.Add(new DlpCompletenessRow(consumer.Id, consumer.AccountNumber, consumer.MeterId, consumer.MeterNumber, profileDate, DlpCompletenessStatus.Missing));
                continue;
            }

            if (profiles.Count > 1)
            {
                rows.Add(new DlpCompletenessRow(consumer.Id, consumer.AccountNumber, consumer.MeterId, consumer.MeterNumber, profileDate, DlpCompletenessStatus.Duplicate));
                continue;
            }

            var profile = profiles[0];
            var status = profile.Status switch
            {
                DailyProfileStatus.Rejected => DlpCompletenessStatus.Invalid,
                _ when profile.IsProvisional => DlpCompletenessStatus.Provisional,
                _ => DlpCompletenessStatus.Complete,
            };
            rows.Add(new DlpCompletenessRow(consumer.Id, consumer.AccountNumber, consumer.MeterId, consumer.MeterNumber, profileDate, status));
        }

        return rows;
    }

    public async Task<IReadOnlyList<EnergyValidationOutcome>> EvaluateEnergyValidationAsync(
        Guid consumerId, Guid meterId, DateOnly validationDate, CancellationToken cancellationToken = default)
    {
        var evaluatedAt = DateTime.UtcNow;
        var outcomes = new List<EnergyValidationOutcome>();

        // DLP total for the date, if any (the common comparison anchor for both BP and LS).
        var dlp = await _db.DailyLoadProfiles.FirstOrDefaultAsync(
            d => d.ConsumerId == consumerId && d.MeterId == meterId && d.ProfileDate == validationDate
                && d.Status != DailyProfileStatus.Rejected,
            cancellationToken);

        // BP delta for the date: closing reading at/after the date's end minus the latest opening
        // reading at/before the date's start, both scoped to this MeterId (meter-swap boundary
        // respected implicitly — BP readings never cross a MeterId).
        var dayStart = validationDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        var opening = await _db.RegisterReadings
            .Where(r => r.ConsumerId == consumerId && r.MeterId == meterId && r.ReadingTimestamp <= dayStart && r.Status != RegisterReadingStatus.Rejected)
            .OrderByDescending(r => r.ReadingTimestamp)
            .FirstOrDefaultAsync(cancellationToken);
        var closing = await _db.RegisterReadings
            .Where(r => r.ConsumerId == consumerId && r.MeterId == meterId && r.ReadingTimestamp >= dayEnd && r.Status != RegisterReadingStatus.Rejected)
            .OrderBy(r => r.ReadingTimestamp)
            .FirstOrDefaultAsync(cancellationToken);
        decimal? bpDeltaKwh = (opening is not null && closing is not null) ? closing.CumulativeImportKwh - opening.CumulativeImportKwh : null;

        // LS aggregated total for the date.
        var lsIntervals = await _db.LoadSurveyIntervals
            .Where(l => l.ConsumerId == consumerId && l.MeterId == meterId && l.IntervalStart >= dayStart && l.IntervalStart < dayEnd)
            .ToListAsync(cancellationToken);
        decimal? lsTotalKwh = lsIntervals.Count > 0 ? lsIntervals.Sum(l => l.ImportKwh) : null;

        if (dlp is not null && bpDeltaKwh is not null)
            outcomes.Add(await UpsertResultAsync(consumerId, meterId, validationDate, EnergyValidationRule.BpVsDlp, dlp.TotalKwh, bpDeltaKwh.Value, evaluatedAt, cancellationToken));

        if (dlp is not null && lsTotalKwh is not null)
            outcomes.Add(await UpsertResultAsync(consumerId, meterId, validationDate, EnergyValidationRule.LsVsDlp, dlp.TotalKwh, lsTotalKwh.Value, evaluatedAt, cancellationToken));

        if (bpDeltaKwh is not null && lsTotalKwh is not null)
            outcomes.Add(await UpsertResultAsync(consumerId, meterId, validationDate, EnergyValidationRule.BpVsLs, bpDeltaKwh.Value, lsTotalKwh.Value, evaluatedAt, cancellationToken));

        if (outcomes.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return outcomes;
    }

    private async Task<EnergyValidationOutcome> UpsertResultAsync(
        Guid consumerId, Guid meterId, DateOnly validationDate, EnergyValidationRule rule,
        decimal expectedKwh, decimal actualKwh, DateTime evaluatedAt, CancellationToken cancellationToken)
    {
        var variancePct = expectedKwh == 0 ? 0 : Math.Round(Math.Abs(actualKwh - expectedKwh) / expectedKwh * 100m, 2);
        var (status, reason) = variancePct >= _options.FailTolerancePct
            ? (EnergyValidationStatus.Fail, $"Variance {variancePct}% exceeds the fail tolerance of {_options.FailTolerancePct}%.")
            : variancePct >= _options.WarningTolerancePct
                ? (EnergyValidationStatus.Warning, $"Variance {variancePct}% exceeds the warning tolerance of {_options.WarningTolerancePct}%.")
                : (EnergyValidationStatus.Pass, $"Variance {variancePct}% is within the {_options.WarningTolerancePct}% warning tolerance.");

        var existing = await _db.EnergyValidationResults.FirstOrDefaultAsync(
            v => v.ConsumerId == consumerId && v.MeterId == meterId && v.ValidationDate == validationDate && v.Rule == rule,
            cancellationToken);

        if (existing is not null)
        {
            existing.Reevaluate(expectedKwh, actualKwh, status, reason, evaluatedAt);
        }
        else
        {
            existing = new EnergyValidationResult(Guid.NewGuid(), consumerId, meterId, validationDate, rule, expectedKwh, actualKwh, status, reason, evaluatedAt);
            _db.EnergyValidationResults.Add(existing);
        }

        return new EnergyValidationOutcome(
            existing.Id, consumerId, meterId, validationDate, rule, existing.ExpectedValueKwh, existing.ActualValueKwh,
            existing.VarianceKwh, existing.VariancePct, existing.Status, existing.Reason);
    }
}
