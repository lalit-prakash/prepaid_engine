using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.Tariffs;

public sealed record ActivatedTariff(Guid ChangeRequestId, Guid NewTariffId, string Name);
public sealed record FailedActivation(Guid ChangeRequestId, string Error);
public sealed record TariffActivationResult(IReadOnlyList<ActivatedTariff> Activated, IReadOnlyList<FailedActivation> Failed);

/// <summary>
/// Activates every Scheduled <see cref="TariffChangeRequest"/> whose commencement date has arrived:
/// materializes the new immutable <see cref="Tariff"/> row and retires the superseded one, so bills
/// already generated keep pointing at the exact rates they were calculated with.
///
/// Each request is activated in its own save. A request that cannot be activated (for example a
/// name clash) is reported and skipped; it never blocks the others, and it stays Scheduled so it is
/// visible rather than silently lost. Safe to run repeatedly: a request can only be Activated once.
/// </summary>
public sealed class TariffActivationService
{
    private readonly PrepaidEngineDbContext _db;
    private readonly ILogger<TariffActivationService> _logger;

    public TariffActivationService(PrepaidEngineDbContext db, ILogger<TariffActivationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TariffActivationResult> ActivateDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var today = nowUtc.Date;
        var dueIds = await _db.TariffChangeRequests.AsNoTracking()
            .Where(r => r.Status == TariffChangeRequestStatus.Scheduled && r.CommencementDate!.Value.Date <= today)
            .OrderBy(r => r.CommencementDate).ThenBy(r => r.ApprovedAt)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var activated = new List<ActivatedTariff>();
        var failed = new List<FailedActivation>();

        foreach (var id in dueIds)
        {
            try
            {
                var result = await ActivateOneAsync(id, nowUtc, cancellationToken);
                if (result is not null) activated.Add(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Drop whatever this request left half-tracked so the next one starts clean.
                _db.ChangeTracker.Clear();
                _logger.LogError(ex, "Could not activate tariff change request {RequestId}; it stays Scheduled.", id);
                failed.Add(new FailedActivation(id, ex.GetBaseException().Message));
            }
        }

        return new TariffActivationResult(activated, failed);
    }

    private async Task<ActivatedTariff?> ActivateOneAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var request = await _db.TariffChangeRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null || request.Status != TariffChangeRequestStatus.Scheduled)
            return null;

        // Owned collections must be read without tracking: EF cannot track an owned type queried on
        // its own without its owner in the same result.
        var slabs = await _db.Entry(request).Collection(r => r.ProposedSlabs).Query().AsNoTracking().ToListAsync(cancellationToken);
        var touPeriods = await _db.Entry(request).Collection(r => r.ProposedTouPeriods).Query().AsNoTracking().ToListAsync(cancellationToken);

        var newTariff = new Tariff(
            Guid.NewGuid(), request.ProposedName, request.ProposedCategory,
            slabs.Select(s => new TariffSlab(s.FromKwh, s.UpToKwh, s.RatePerKwh)),
            request.ProposedFixedChargePerUnitPerMonth, request.ProposedPrepaidEnergyRebatePercent, request.ProposedEmergencyCreditLimit,
            request.ProposedMinVendAmountSinglePhase, request.ProposedMaxVendAmountSinglePhase,
            request.ProposedMinVendAmountThreePhase, request.ProposedMaxVendAmountThreePhase,
            touPeriods.Select(p => new TouPeriod(p.Label, p.StartTime, p.EndTime, p.RatePerKvah)));

        // A revision replaces exactly one Active tariff. If that tariff was already retired (by another
        // revision that activated first) this request is stale and must not create a second Active one.
        if (request.SupersedesTariffId.HasValue)
        {
            var superseded = await _db.Tariffs.FirstOrDefaultAsync(t => t.Id == request.SupersedesTariffId.Value, cancellationToken);
            if (superseded is null || superseded.Status != TariffLifecycleStatus.Active)
                throw new InvalidOperationException("The tariff this change revises is no longer Active; cancel this request and revise the current tariff.");
            superseded.Retire();
            newTariff.InheritClassification(superseded);
        }
        _db.Tariffs.Add(newTariff);

        request.Activate(newTariff.Id, nowUtc);
        _db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), nameof(TariffChangeRequest), request.Id.ToString(), "ACTIVATED", "system", nowUtc,
            details: $"New tariff {newTariff.Id} ('{newTariff.Name}') is now active."));
        if (request.SupersedesTariffId.HasValue)
            _db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), nameof(Tariff), request.SupersedesTariffId.Value.ToString(), "RETIRED", "system", nowUtc));

        // One save = one transaction: retire + create + status change land together or not at all.
        await _db.SaveChangesAsync(cancellationToken);
        return new ActivatedTariff(request.Id, newTariff.Id, newTariff.Name);
    }
}
