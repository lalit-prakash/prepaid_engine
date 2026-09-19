using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Api.Security;
using PrepaidEngine.Api.Dashboard;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Application.Conversion;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Application.MeterData;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Conversion;
using PrepaidEngine.Infrastructure.MeterCommands;
using PrepaidEngine.Infrastructure.MeterData;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Sla;
using PrepaidEngine.Infrastructure.Tariffs;
using PrepaidEngine.Application.Sla;
using PrepaidEngine.Infrastructure.Persistence.Seed;
using PrepaidEngine.Infrastructure.Rms;
using static PrepaidEngine.Api.ApiHelpers;

namespace PrepaidEngine.Api.Endpoints;

/// <summary>Tariff endpoints, moved out of Program.cs unchanged.</summary>
public static class TariffEndpoints
{
    public static void MapTariffEndpoints(this WebApplication app)
    {
        // Tariff Management read endpoints — the real Tariff/TariffSlab/TouPeriod configuration this
        // engine actually bills against (see docs/tariff-validation-report.md for sourcing). A Tariff
        // row itself is still never edited in place — see TariffChangeRequest's doc comment for the
        // governance workflow that now exists to create a new one instead.
        app.MapGet("/api/v1/tariffs", async (HttpContext http, PrepaidEngine.Domain.Enums.TariffLifecycleStatus? status, PrepaidEngineDbContext db) =>
        {
            var query = db.Tariffs.AsQueryable();
            if (status.HasValue) query = query.Where(t => t.Status == status.Value);

            var tariffs = await query
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Category,
                    t.Status,
                    t.FixedChargePerUnitPerMonth,
                    t.PrepaidEnergyRebatePercent,
                    t.EmergencyCreditLimit,
                    SlabCount = t.Slabs.Count,
                    TouPeriodCount = t.TouPeriods.Count,
                })
                .ToCappedListAsync(http);

            return Results.Ok(tariffs);
        })
        .WithName("ListTariffs")
        .RequireAuthorization();


        app.MapGet("/api/v1/tariffs/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
        {
            var tariff = await db.Tariffs
                .Include(t => t.Slabs)
                .Include(t => t.TouPeriods)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tariff is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                tariff.Id,
                tariff.Name,
                tariff.Category,
                tariff.Status,
                tariff.FixedChargePerUnitPerMonth,
                tariff.PrepaidEnergyRebatePercent,
                tariff.EmergencyCreditLimit,
                tariff.MinVendAmountSinglePhase,
                tariff.MaxVendAmountSinglePhase,
                tariff.MinVendAmountThreePhase,
                tariff.MaxVendAmountThreePhase,
                Slabs = tariff.Slabs
                    .OrderBy(s => s.FromKwh)
                    .Select(s => new { s.Id, s.FromKwh, s.UpToKwh, s.RatePerKwh }),
                TouPeriods = tariff.TouPeriods
                    .OrderBy(p => p.StartTime)
                    .Select(p => new { p.Id, p.Label, StartTime = p.StartTime.ToString(), EndTime = p.EndTime.ToString(), p.RatePerKvah }),
            });
        })
        .WithName("GetTariffById")
        .RequireAuthorization();


        // Version lineage of a tariff: the chain of immutable Tariff rows linked by activated
        // TariffChangeRequests (oldest first), plus any still-open change requests against this tariff.
        // A Tariff row is never edited, so each entry is exactly what bills against it were calculated with.
        app.MapGet("/api/v1/tariffs/{id:guid}/lineage", async (Guid id, PrepaidEngineDbContext db) =>
        {
            if (!await db.Tariffs.AnyAsync(t => t.Id == id))
                return Results.NotFound();

            // Walk back to the oldest ancestor, then forward, bounded so a bad data cycle cannot loop.
            const int maxHops = 50;
            var chainIds = new List<Guid> { id };
            var cursor = id;
            for (var i = 0; i < maxHops; i++)
            {
                var parent = await db.TariffChangeRequests.AsNoTracking()
                    .Where(r => r.ResultingTariffId == cursor && r.SupersedesTariffId != null)
                    .Select(r => r.SupersedesTariffId)
                    .FirstOrDefaultAsync();
                if (parent is null || chainIds.Contains(parent.Value)) break;
                chainIds.Insert(0, parent.Value);
                cursor = parent.Value;
            }
            cursor = id;
            for (var i = 0; i < maxHops; i++)
            {
                var child = await db.TariffChangeRequests.AsNoTracking()
                    .Where(r => r.SupersedesTariffId == cursor && r.Status == TariffChangeRequestStatus.Activated)
                    .Select(r => r.ResultingTariffId)
                    .FirstOrDefaultAsync();
                if (child is null || chainIds.Contains(child.Value)) break;
                chainIds.Add(child.Value);
                cursor = child.Value;
            }

            var tariffs = await db.Tariffs.AsNoTracking().Where(t => chainIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Name, t.Status, t.FixedChargePerUnitPerMonth, t.PrepaidEnergyRebatePercent })
                .ToListAsync();
            var requests = await db.TariffChangeRequests.AsNoTracking()
                .Where(r => r.Status == TariffChangeRequestStatus.Activated
                    && ((r.ResultingTariffId != null && chainIds.Contains(r.ResultingTariffId.Value))
                        || (r.SupersedesTariffId != null && chainIds.Contains(r.SupersedesTariffId.Value))))
                .ToListAsync();

            var versions = chainIds.Select((tid, index) =>
            {
                var t = tariffs.First(x => x.Id == tid);
                var created = requests.FirstOrDefault(r => r.ResultingTariffId == tid);
                var superseding = requests.FirstOrDefault(r => r.SupersedesTariffId == tid);
                return new
                {
                    VersionNumber = index + 1,
                    TariffId = t.Id,
                    t.Name,
                    t.Status,
                    t.FixedChargePerUnitPerMonth,
                    t.PrepaidEnergyRebatePercent,
                    IsCurrentlyViewed = tid == id,
                    ChangeRequestId = created?.Id,
                    ChangeReason = created?.ChangeReason,
                    SubmittedBy = created?.SubmittedBy,
                    ApprovedBy = created?.ApprovedBy,
                    CommencementDate = created?.CommencementDate,
                    EffectiveFrom = created?.ActivatedAt,
                    RetiredAt = superseding?.ActivatedAt,
                };
            }).ToList();

            var openStatuses = new[]
            {
                TariffChangeRequestStatus.Draft, TariffChangeRequestStatus.PendingApproval,
                TariffChangeRequestStatus.Rejected, TariffChangeRequestStatus.Scheduled,
            };
            var openChanges = await db.TariffChangeRequests.AsNoTracking()
                .Where(r => r.SupersedesTariffId == id && openStatuses.Contains(r.Status))
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new { r.Id, r.Status, r.ProposedName, r.CreatedBy, r.SubmittedAt, r.CommencementDate })
                .ToListAsync();

            return Results.Ok(new { versions, openChanges });
        })
        .WithName("GetTariffLineage")
        .RequireAuthorization();


        // --- Tariff governance (Phase 1: Tariff Governance + MDM Recharge Command Integration) --------
        // See TariffChangeRequest's own doc comment for the full workflow. IT (CREATE/EDIT/DRAFT/SUBMIT)
        // and Utility (APPROVE/REJECT) are role-gated at the endpoint level — the primary defense against
        // self-approval — with a same-actor check inside TariffChangeRequest.Approve() as defense in depth.

        app.MapPost("/api/v1/tariff-change-requests", async (CreateTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            var actor = user.Identity?.Name ?? "unknown";

            var conflict = await TariffChangeConflictAsync(db, null, request.SupersedesTariffId, request.ProposedName);
            if (conflict is not null)
                return Results.Conflict(new { error = conflict });

            TariffChangeRequest changeRequest;
            try
            {
                changeRequest = new TariffChangeRequest(
                    Guid.NewGuid(), request.SupersedesTariffId, request.ProposedName, request.ProposedCategory,
                    request.ProposedSlabs.Select(s => new TariffSlab(s.FromKwh, s.UpToKwh, s.RatePerKwh)),
                    request.ProposedFixedChargePerUnitPerMonth, request.ProposedPrepaidEnergyRebatePercent, request.ProposedEmergencyCreditLimit,
                    actor, DateTime.UtcNow,
                    request.ProposedMinVendAmountSinglePhase, request.ProposedMaxVendAmountSinglePhase,
                    request.ProposedMinVendAmountThreePhase, request.ProposedMaxVendAmountThreePhase,
                    request.ProposedTouPeriods?.Select(p => new TouPeriod(p.Label, TimeSpan.Parse(p.StartTime), TimeSpan.Parse(p.EndTime), p.RatePerKvah)));
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            db.TariffChangeRequests.Add(changeRequest);
            Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "TARIFF_CREATED", actor,
                details: $"Proposed '{changeRequest.ProposedName}'" + (request.SupersedesTariffId.HasValue ? $" revising tariff {request.SupersedesTariffId}." : " as a new tariff."));

            await db.SaveChangesAsync();

            return Results.Ok(new { changeRequest.Id, changeRequest.Status });
        })
        .WithName("CreateTariffChangeRequest")
        .RequireAuthorization("ITRole");


        app.MapPut("/api/v1/tariff-change-requests/{id:guid}/draft", async (Guid id, UpdateTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            var actor = user.Identity?.Name ?? "unknown";
            var changeRequest = await db.TariffChangeRequests
                .Include(r => r.ProposedSlabs).Include(r => r.ProposedTouPeriods)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (changeRequest is null)
                return Results.NotFound();

            try
            {
                changeRequest.UpdateProposal(
                    request.ProposedName, request.ProposedCategory,
                    request.ProposedSlabs.Select(s => new TariffSlab(s.FromKwh, s.UpToKwh, s.RatePerKwh)),
                    request.ProposedFixedChargePerUnitPerMonth, request.ProposedPrepaidEnergyRebatePercent, request.ProposedEmergencyCreditLimit,
                    request.ProposedMinVendAmountSinglePhase, request.ProposedMaxVendAmountSinglePhase,
                    request.ProposedMinVendAmountThreePhase, request.ProposedMaxVendAmountThreePhase,
                    request.ProposedTouPeriods?.Select(p => new TouPeriod(p.Label, TimeSpan.Parse(p.StartTime), TimeSpan.Parse(p.EndTime), p.RatePerKvah)));
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "DRAFT_SAVED", actor);
            await db.SaveChangesAsync();

            return Results.Ok(new { changeRequest.Id, changeRequest.Status });
        })
        .WithName("UpdateTariffChangeRequestDraft")
        .RequireAuthorization("ITRole");


        app.MapPost("/api/v1/tariff-change-requests/{id:guid}/submit", async (Guid id, SubmitTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            var actor = user.Identity?.Name ?? "unknown";
            var changeRequest = await db.TariffChangeRequests
                .Include(r => r.ProposedSlabs).Include(r => r.ProposedTouPeriods)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (changeRequest is null)
                return Results.NotFound();

            var validationErrors = changeRequest.ValidateForSubmission();
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { errors = validationErrors });

            var conflict = await TariffChangeConflictAsync(db, changeRequest.Id, changeRequest.SupersedesTariffId, changeRequest.ProposedName);
            if (conflict is not null)
                return Results.Conflict(new { error = conflict });

            try
            {
                changeRequest.Submit(actor, request.ChangeReason, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "SUBMITTED", actor, details: request.ChangeReason);
            await db.SaveChangesAsync();

            return Results.Ok(new { changeRequest.Id, changeRequest.Status });
        })
        .WithName("SubmitTariffChangeRequest")
        .RequireAuthorization("ITRole");


        app.MapGet("/api/v1/tariff-change-requests", async (HttpContext http, TariffChangeRequestStatus? status, PrepaidEngineDbContext db) =>
        {
            var query = db.TariffChangeRequests.AsQueryable();
            if (status.HasValue) query = query.Where(r => r.Status == status.Value);

            var requests = await query
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id, r.SupersedesTariffId, r.ResultingTariffId, r.ProposedName, r.ProposedCategory, r.Status,
                    r.CreatedBy, r.CreatedAt, r.ChangeReason, r.SubmittedBy, r.SubmittedAt,
                    r.ApprovedBy, r.ApprovedAt, r.CommencementDate, r.RejectedBy, r.RejectedAt, r.RejectionReason, r.ActivatedAt,
                })
                .Take(500)
                .ToCappedListAsync(http);

            return Results.Ok(requests);
        })
        .WithName("ListTariffChangeRequests")
        .RequireAuthorization();


        app.MapGet("/api/v1/tariff-change-requests/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
        {
            var changeRequest = await db.TariffChangeRequests
                .Include(r => r.ProposedSlabs).Include(r => r.ProposedTouPeriods)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (changeRequest is null)
                return Results.NotFound();

            Tariff? currentTariff = changeRequest.SupersedesTariffId.HasValue
                ? await db.Tariffs.Include(t => t.Slabs).Include(t => t.TouPeriods).FirstOrDefaultAsync(t => t.Id == changeRequest.SupersedesTariffId.Value)
                : null;

            return Results.Ok(new
            {
                changeRequest.Id,
                changeRequest.SupersedesTariffId,
                changeRequest.ResultingTariffId,
                changeRequest.Status,
                Proposed = new
                {
                    changeRequest.ProposedName,
                    changeRequest.ProposedCategory,
                    changeRequest.ProposedFixedChargePerUnitPerMonth,
                    changeRequest.ProposedPrepaidEnergyRebatePercent,
                    changeRequest.ProposedEmergencyCreditLimit,
                    changeRequest.ProposedMinVendAmountSinglePhase,
                    changeRequest.ProposedMaxVendAmountSinglePhase,
                    changeRequest.ProposedMinVendAmountThreePhase,
                    changeRequest.ProposedMaxVendAmountThreePhase,
                    Slabs = changeRequest.ProposedSlabs.OrderBy(s => s.FromKwh).Select(s => new { s.FromKwh, s.UpToKwh, s.RatePerKwh }),
                    TouPeriods = changeRequest.ProposedTouPeriods.Select(p => new { p.Label, StartTime = p.StartTime.ToString(), EndTime = p.EndTime.ToString(), p.RatePerKvah }),
                },
                Current = currentTariff == null ? null : new
                {
                    currentTariff.Name,
                    currentTariff.Category,
                    currentTariff.FixedChargePerUnitPerMonth,
                    currentTariff.PrepaidEnergyRebatePercent,
                    currentTariff.EmergencyCreditLimit,
                    currentTariff.MinVendAmountSinglePhase,
                    currentTariff.MaxVendAmountSinglePhase,
                    currentTariff.MinVendAmountThreePhase,
                    currentTariff.MaxVendAmountThreePhase,
                    Slabs = currentTariff.Slabs.OrderBy(s => s.FromKwh).Select(s => new { s.FromKwh, s.UpToKwh, s.RatePerKwh }),
                    TouPeriods = currentTariff.TouPeriods.Select(p => new { p.Label, StartTime = p.StartTime.ToString(), EndTime = p.EndTime.ToString(), p.RatePerKvah }),
                },
                changeRequest.CreatedBy,
                changeRequest.CreatedAt,
                changeRequest.ChangeReason,
                changeRequest.SubmittedBy,
                changeRequest.SubmittedAt,
                changeRequest.ApprovedBy,
                changeRequest.ApprovedAt,
                changeRequest.CommencementDate,
                changeRequest.RejectedBy,
                changeRequest.RejectedAt,
                changeRequest.RejectionReason,
                changeRequest.ActivatedAt,
            });
        })
        .WithName("GetTariffChangeRequestById")
        .RequireAuthorization();


        app.MapPost("/api/v1/tariff-change-requests/{id:guid}/approve", async (Guid id, ApproveTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            var actor = user.Identity?.Name ?? "unknown";
            var changeRequest = await db.TariffChangeRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (changeRequest is null)
                return Results.NotFound();

            var conflict = await TariffChangeConflictAsync(db, changeRequest.Id, changeRequest.SupersedesTariffId, changeRequest.ProposedName);
            if (conflict is not null)
                return Results.Conflict(new { error = conflict });

            var commencementDate = DateTime.SpecifyKind(request.CommencementDate.Date, DateTimeKind.Utc);

            try
            {
                changeRequest.Approve(actor, commencementDate, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "APPROVED", actor,
                details: $"Commencement {commencementDate:d}");
            await db.SaveChangesAsync();

            return Results.Ok(new { changeRequest.Id, changeRequest.Status, changeRequest.CommencementDate });
        })
        .WithName("ApproveTariffChangeRequest")
        .RequireAuthorization("UtilityRole");


        app.MapPost("/api/v1/tariff-change-requests/{id:guid}/reject", async (Guid id, RejectTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            var actor = user.Identity?.Name ?? "unknown";
            var changeRequest = await db.TariffChangeRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (changeRequest is null)
                return Results.NotFound();

            try
            {
                changeRequest.Reject(actor, request.RejectionReason, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "REJECTED", actor, details: request.RejectionReason);
            await db.SaveChangesAsync();

            return Results.Ok(new { changeRequest.Id, changeRequest.Status });
        })
        .WithName("RejectTariffChangeRequest")
        .RequireAuthorization("UtilityRole");


        // Manual trigger for the same activation the TariffActivationWorker performs every minute; useful to
        // activate a change on demand instead of waiting up to a minute. Each due request is activated in its
        // own transaction, so one that cannot activate is reported in `failed` and never blocks the rest.
        app.MapPost("/api/v1/tariff-change-requests/activate-due", async (TariffActivationService activation, CancellationToken cancellationToken) =>
            Results.Ok(await activation.ActivateDueAsync(DateTime.UtcNow, cancellationToken)))
        .WithName("ActivateDueTariffChangeRequests")
        .RequireAuthorization("UtilityRole");


        // Cancels a change request that will not proceed (including an approved one that is Scheduled and
        // would otherwise sit until its commencement date). IT may cancel its own Draft/Rejected requests;
        // Utility may cancel PendingApproval/Scheduled ones. A reason is mandatory and audited.
        app.MapPost("/api/v1/tariff-change-requests/{id:guid}/cancel", async (Guid id, CancelTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required to cancel a change request." });

            var actor = user.Identity?.Name ?? "unknown";
            var changeRequest = await db.TariffChangeRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (changeRequest is null)
                return Results.NotFound();

            var isUtility = user.IsInRole(nameof(UserRole.Utility));
            var allowed = isUtility
                ? changeRequest.Status is TariffChangeRequestStatus.PendingApproval or TariffChangeRequestStatus.Scheduled
                : changeRequest.Status is TariffChangeRequestStatus.Draft or TariffChangeRequestStatus.Rejected;
            if (!allowed)
                return Results.Json(new { error = $"A {(isUtility ? "Utility" : "IT")} user cannot cancel a request that is {changeRequest.Status}." },
                    statusCode: StatusCodes.Status403Forbidden);

            try
            {
                changeRequest.Cancel();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "CANCELLED", actor, details: request.Reason);
            await db.SaveChangesAsync();
            return Results.Ok(new { changeRequest.Id, changeRequest.Status });
        })
        .WithName("CancelTariffChangeRequest")
        .RequireAuthorization("TariffGovernanceRole");


        // Calculation Workbench — a SIMULATION-ONLY preview of a charge calculation for an arbitrary
        // (tariff, consumption, load) combination, not tied to any real consumer/bill. Delegates every
        // figure to the same domain methods (Tariff.CalculateEnergyCharge/CalculateFixedCharge/
        // CalculateDailyFixedCharge, ElectricityDuty.Calculate) that production billing uses — the
        // frontend must not duplicate this arithmetic itself (see docs/ARCHITECTURE.md's frontend
        // calculation rule), it only renders whatever this endpoint returns.
        app.MapPost("/api/v1/calculation-workbench/simulate", async (SimulateChargeRequest request, PrepaidEngineDbContext db) =>
        {
            if (request.ConsumptionKwh < 0)
                return Results.BadRequest(new { error = "Consumption cannot be negative." });
            if (request.ConnectedLoadOrContractDemand < 0)
                return Results.BadRequest(new { error = "Connected load / contract demand cannot be negative." });

            var tariff = await db.Tariffs.Include(t => t.Slabs).FirstOrDefaultAsync(t => t.Id == request.TariffId);
            if (tariff is null)
                return Results.NotFound(new { error = $"No tariff found with id '{request.TariffId}'." });

            if (tariff.Slabs.Count == 0)
            {
                // Pure-ToD tariffs (IHT/IEHT) have no ordinary kWh slabs — CalculateEnergyCharge would
                // silently return 0 for them, which would misrepresent a real charge as zero rather
                // than reporting that this simulator doesn't support ToD-only tariffs yet.
                return Results.BadRequest(new
                {
                    error = $"Tariff '{tariff.Name}' has no ordinary energy slabs (it is ToD-only) — this simulator does not yet support ToD-based simulation.",
                });
            }

            var grossEnergyCharge = tariff.CalculateEnergyCharge(request.ConsumptionKwh);
            var rebateAmount = grossEnergyCharge * (tariff.PrepaidEnergyRebatePercent / 100m);
            var netEnergyCharge = grossEnergyCharge - rebateAmount;
            var fixedChargeMonthly = tariff.CalculateFixedCharge(request.ConnectedLoadOrContractDemand);
            var fixedChargeDaily = tariff.CalculateDailyFixedCharge(request.ConnectedLoadOrContractDemand);
            var electricityDuty = ElectricityDuty.Calculate(tariff.Category, request.ConsumptionKwh);
            var totalMonthlyCharge = netEnergyCharge + fixedChargeMonthly + electricityDuty;

            return Results.Ok(new
            {
                Simulation = true,
                Tariff = new { tariff.Id, tariff.Name, tariff.Category },
                Inputs = new { request.ConsumptionKwh, request.ConnectedLoadOrContractDemand },
                GrossEnergyCharge = grossEnergyCharge,
                PrepaidRebatePercent = tariff.PrepaidEnergyRebatePercent,
                RebateAmount = rebateAmount,
                NetEnergyCharge = netEnergyCharge,
                FixedChargeMonthly = fixedChargeMonthly,
                FixedChargeDaily = fixedChargeDaily,
                ElectricityDuty = electricityDuty,
                TotalMonthlyCharge = totalMonthlyCharge,
            });
        })
        .WithName("SimulateCharge")
        .RequireAuthorization("Authenticated");


        // --- Tariff version history: append-only record of parameter changes, enabling a Tariff -------
        // Change Report even though Tariff itself still only exposes its single current version (see
        // README's Tariff Management section for why there is no tariff update endpoint yet).
        app.MapPost("/api/v1/tariffs/{id:guid}/versions", async (Guid id, TariffVersionRequest request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            var tariff = await db.Tariffs.FirstOrDefaultAsync(t => t.Id == id);
            if (tariff is null)
                return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.ChangeNote))
                return Results.BadRequest(new { error = "A change note is required to record a tariff version." });

            TariffVersion version;
            try
            {
                version = new TariffVersion(Guid.NewGuid(), tariff.Id, request.FieldName, request.OldValue, request.NewValue,
                    request.ChangeNote, request.EffectiveDate, DateTime.UtcNow);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            db.TariffVersions.Add(version);
            Audit(db, nameof(Tariff), tariff.Id.ToString(), "VersionRecorded", user.Identity?.Name ?? "unknown",
                oldValue: request.OldValue, newValue: request.NewValue, details: $"{request.FieldName}: {request.ChangeNote}");

            await db.SaveChangesAsync();

            return Results.Ok(new { version.Id, version.TariffId, version.FieldName, version.OldValue, version.NewValue, version.EffectiveDate, version.RecordedAt });
        })
        .WithName("RecordTariffVersion")
        .RequireAuthorization("ITRole");


        app.MapGet("/api/v1/tariffs/{id:guid}/versions", async (Guid id, PrepaidEngineDbContext db) =>
        {
            var tariffExists = await db.Tariffs.AnyAsync(t => t.Id == id);
            if (!tariffExists)
                return Results.NotFound();

            var versions = await db.TariffVersions
                .Where(v => v.TariffId == id)
                .OrderByDescending(v => v.EffectiveDate)
                .Select(v => new { v.Id, v.FieldName, v.OldValue, v.NewValue, v.ChangeNote, v.EffectiveDate, v.RecordedAt })
                .ToListAsync();

            return Results.Ok(versions);
        })
        .WithName("ListTariffVersions")
        .RequireAuthorization();
    }
}
