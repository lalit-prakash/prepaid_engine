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
                    t.ScheduleCode,
                    t.VoltageLevel,
                    t.EnergyUnit,
                    t.FixedChargeBasis,
                    FirstSlabRate = t.Slabs.OrderBy(sl => sl.FromKwh).Select(sl => (decimal?)sl.RatePerKwh).FirstOrDefault(),
                    NormalTouRate = t.TouPeriods.Where(p => p.Label == "Normal").Select(p => (decimal?)p.RatePerKvah).FirstOrDefault(),
                    SlabCount = t.Slabs.Count,
                    TouPeriodCount = t.TouPeriods.Count,
                })
                .ToCappedListAsync(http);

            return Results.Ok(tariffs);
        })
        .WithName("ListTariffs")
        .RequireAuthorization();


        // Each FY 2026-27 book schedule against the tariff in force for it: Matches, Differs (with the fields) or Missing. Read-only.
        app.MapGet("/api/v1/tariffs/book-check", async (PrepaidEngineDbContext db) =>
        {
            var active = await db.Tariffs.AsNoTracking().Include(t => t.Slabs).Include(t => t.TouPeriods).Where(t => t.Status == PrepaidEngine.Domain.Enums.TariffLifecycleStatus.Active).ToListAsync();
            var rows = PrepaidEngine.Infrastructure.Persistence.Seed.TariffBookCheck.Run(active);
            return Results.Ok(new
            {
                Book = "MePDCL Electricity Distribution Tariff FY 2026-27, effective 1 April 2026",
                Matches = rows.Count(r => r.Status == "Matches"),
                Differs = rows.Count(r => r.Status == "Differs"),
                Missing = rows.Count(r => r.Status == "Missing"),
                Rows = rows,
            });
        })
        .WithName("TariffBookCheck")
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
                tariff.ScheduleCode,
                tariff.VoltageLevel,
                tariff.EnergyUnit,
                tariff.FixedChargeBasis,
                tariff.MinimumChargeableDemand,
                tariff.InitialCreditSinglePhase,
                tariff.InitialCreditThreePhase,
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


        // Calculation Workbench — a SIMULATION-ONLY preview of one day's prepaid bill for an arbitrary (tariff, consumption,
        // load) combination, not tied to any real consumer, bill or wallet. It calls the same DailyBillCalculator the daily billing
        // run debits from, so what it shows is what a real day would be charged — the frontend must not repeat this arithmetic
        // (see docs/ARCHITECTURE.md's frontend calculation rule); it only renders what this endpoint returns.
        app.MapPost("/api/v1/calculation-workbench/simulate", async (SimulateChargeRequest request, PrepaidEngineDbContext db) =>
        {
            if (request.ConsumptionKwh < 0)
                return Results.BadRequest(new { error = "Consumption cannot be negative." });
            if (request.DayKvah < 0 || request.MonthToDateKvah < 0)
                return Results.BadRequest(new { error = "kVAh cannot be negative." });
            if (request.MonthToDateKwh < 0)
                return Results.BadRequest(new { error = "Month-to-date consumption cannot be negative." });
            if (request.ConnectedLoadOrContractDemand < 0)
                return Results.BadRequest(new { error = "Connected load / contract demand cannot be negative." });
            if (request.TmcMonthly < 0 || request.CpmcMonthly < 0)
                return Results.BadRequest(new { error = "Maintenance charges cannot be negative." });
            if (request.DaysInMonth is < 28 or > 31)
                return Results.BadRequest(new { error = "Days in the month must be from 28 to 31." });

            var tariff = await db.Tariffs.Include(t => t.Slabs).Include(t => t.TouPeriods).FirstOrDefaultAsync(t => t.Id == request.TariffId);
            if (tariff is null)
                return Results.NotFound(new { error = $"No tariff found with id '{request.TariffId}'." });

            if (request.TouKvahByBand is { Count: > 0 })
            {
                if (tariff.TouPeriods.Count == 0)
                    return Results.BadRequest(new { error = $"Tariff '{tariff.Name}' has no Time-of-Day bands." });
                var known = tariff.TouPeriods.Select(p => p.Label).ToHashSet();
                if (request.TouKvahByBand.Keys.FirstOrDefault(k => !known.Contains(k)) is { } unknown)
                    return Results.BadRequest(new { error = $"'{unknown}' is not a Time-of-Day band on this tariff (bands: {string.Join(", ", known)})." });
                if (request.TouKvahByBand.Values.Any(v => v < 0))
                    return Results.BadRequest(new { error = "Band consumption cannot be negative." });
            }

            var loadKw = request.LoadInHp ? TariffBookParameters.HpToKw(request.ConnectedLoadOrContractDemand) : request.ConnectedLoadOrContractDemand;
            var fppasShare = request.PriorMonthEnergyCharge > 0 && request.FppasRatePercent != 0
                ? new FppasCharge(Guid.NewGuid(), request.PriorMonthEnergyCharge, request.FppasRatePercent / 100m, DateTime.UtcNow).AllocateAcrossDaysRoundedToCents(request.DaysInMonth)[0]
                : 0m;

            var bill = DailyBillCalculator.Calculate(new DailyBillInput(
                tariff, loadKw, request.ConsumptionKwh, request.MonthToDateKwh, request.MeteredOnLtSide, request.TmcMonthly, request.CpmcMonthly, fppasShare, request.TouKvahByBand, request.DayKvah, request.MonthToDateKvah));

            return Results.Ok(new
            {
                Simulation = true,
                Tariff = new
                {
                    tariff.Id, tariff.Name, tariff.Category, tariff.ScheduleCode, tariff.VoltageLevel, tariff.EnergyUnit, tariff.FixedChargeBasis,
                    tariff.FixedChargePerUnitPerMonth, tariff.PrepaidEnergyRebatePercent, tariff.MinimumChargeableDemand,
                    IsTimeOfDay = tariff.Slabs.Count == 0 && tariff.TouPeriods.Count > 0,
                    Bands = tariff.TouPeriods.OrderBy(p => p.StartTime).Select(p => p.Label),
                },
                Inputs = new { request.ConsumptionKwh, request.DayKvah, request.MonthToDateKwh, request.MonthToDateKvah, LoadUsed = loadKw, request.LoadInHp, request.MeteredOnLtSide },
                bill.BilledEnergy,
                bill.BilledUnit,
                bill.GrossEnergyCharge,
                PrepaidRebatePercent = tariff.PrepaidEnergyRebatePercent,
                RebateAmount = bill.PrepaidRebate,
                bill.NetEnergyCharge,
                FixedChargeDaily = bill.FixedCharge,
                FixedChargeMonthly = tariff.CalculateFixedCharge(loadKw),
                ElectricityDuty = bill.ElectricityDuty,
                LtSideMeteringSurcharge = bill.LtSideMeteringSurcharge,
                Tmc = bill.Tmc,
                Cpmc = bill.Cpmc,
                FppasShare = bill.FppasShare,
                Total = bill.Total,
                TotalDebited = bill.TotalRounded,
                bill.Notes,
            });
        })
        .WithName("SimulateCharge")
        .RequireAuthorization("Authenticated");

        // The tariff book's fixed figures (prepaid facilities, duty, maintenance, reconnection, surcharge): one source for what the
        // Tariff & Parameters screen shows and what billing uses.
        app.MapGet("/api/v1/tariff-parameters", () =>
        {
            static object Item(string label, string value, string? note = null) => new { Label = label, Value = value, Note = note };
            string R(decimal v) => "₹" + v.ToString("#,##0.##", System.Globalization.CultureInfo.InvariantCulture);
            string P(decimal v) => (v * 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";

            return Results.Ok(new
            {
                Source = "MePDCL Electricity Distribution Tariff FY 2026-27, effective 1 April 2026 (MSERC Order on Case No. 11 of 2025)",
                Sections = new object[]
                {
                    new { Title = "Prepaid meter facilities (§22)", Items = new[]
                    {
                        Item("Rebate on energy charge", TariffBookParameters.PrepaidEnergyRebatePercent + "%", "All categories. Applied to the energy charge only, not to fixed charge, duty or FPPAS."),
                        Item("Load security deposit", "None", "Not levied on prepaid consumers (§22.2, §1.3.5)."),
                        Item("Emergency credit in the meter", $"{R(TariffBookParameters.EmergencyCreditGeneralPurpose)} General Purpose, {R(TariffBookParameters.EmergencyCreditOthers)} other categories"),
                        Item("Recharge (vend) limits", $"Max {R(15000)} single phase, {R(25000)} three phase; General Purpose {R(50000)} / {R(100000)}, with a minimum of {R(TariffBookParameters.MinimumVendAmountGeneralPurpose)}", "Enforced on every recharge from the consumer's tariff. The book prints the minimum once, beside the General Purpose row, so other categories have none."),
                        Item("Initial credit on installation", $"{R(100)} single phase, {R(200)} three phase; General Purpose {R(1000)} / {R(5000)}", "Adjusted against the first vend."),
                        Item("Consumer friendly credit hours", TariffBookParameters.CreditHours, "Supply continues whatever the balance, so disconnection (manual or automatic) only happens 11:00 AM to 4:00 PM IST. Official holidays are not modelled."),
                        Item("Running out of credit", "Not a disconnection", "The meter cut is not a disconnection; supply resumes on recharge (§13.8), so no reconnection charge applies."),
                    } },
                    new { Title = "How the daily bill is built", Items = new[]
                    {
                        Item("Energy charge", "Slab charge on month-to-date consumption, less the same for the days before", "Slabs continue across the calendar month, so a day that crosses 100 kWh is priced part at each slab."),
                        Item("Fixed charge", "Monthly rate × demand × 12 / 365, every day", "Also on days with no consumption. HT is never billed on less than 56 kVA."),
                        Item("Electricity duty", $"{R(0.05m)} Domestic & BPL; {R(0.06m)} others; Industrial {R(0.05m)} first 15,000 units, {R(0.045m)} next 25,000, {R(0.03m)} after", "Per unit, on consumption, added to each day's bill."),
                        Item("Agriculture load", $"1 HP = {TariffBookParameters.KwPerHp} kW", "Fixed charge is per kW or per HP."),
                        Item("LT-side metering surcharge", P(TariffBookParameters.LtSideMeteringSurchargeRate) + " of energy charges", "Where an HT consumer is metered on the LT side of the transformer, or supplied at a lower voltage than classified."),
                        Item("Time-of-Day (IHT, IEHT)", "Normal 06:00-17:00, Peak 17:00-23:00 (+20%), Off-peak 23:00-06:00 (-15%)", "Needs consumption per band; without it the day is priced at the Normal rate and the bill says so."),
                    } },
                    new { Title = "Maintenance charges (§4, §5), only if opted for", Items = new[]
                    {
                        Item("Transformer (TMC)", $"{R(TransformerMaintenanceCharge.RatePerKvaPerMonth_11kVor33kV)} per kVA per month at 11 kV and 33 kV; {R(TransformerMaintenanceCharge.RatePerKvaPerMonth_132kV)} at 132 kV"),
                        Item("CT-PT set (CPMC)", $"{R(800)} 11 kV 3-wire; {R(1000)} 11 kV 4-wire; {R(1500)} 33 kV 3-wire; {R(1900)} 33 kV 4-wire, per month"),
                    } },
                    new { Title = "Disconnection, reconnection and late payment (§12-§14)", Items = new[]
                    {
                        Item("Reconnection after non-payment", $"{R(TariffBookParameters.ReconnectionChargeSinglePhaseLt)} single phase LT; {R(TariffBookParameters.ReconnectionChargeThreePhaseLtBelow50Kw)} three phase LT below 50 kW; {R(TariffBookParameters.ReconnectionChargeHt)} HT", "Not charged if the bill is paid within one month of disconnection."),
                        Item("Disconnection / reconnection for other reasons", R(TariffBookParameters.DisconnectReconnectFeeOtherThanNonPayment) + " each"),
                        Item("Delayed payment charge", P(TariffBookParameters.DelayedPaymentChargeRatePer30Days) + " per 30 days on the outstanding amount, excluding duty", "Postpaid bills, due 15 days from billing. Not applied to prepaid daily bills."),
                        Item("Interest after disconnection for non-payment", P(TariffBookParameters.DisconnectionInterestRatePerYear) + " a year, simple interest, until reconnection"),
                    } },
                },
            });
        })
        .WithName("TariffBookParameters")
        .RequireAuthorization();


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
