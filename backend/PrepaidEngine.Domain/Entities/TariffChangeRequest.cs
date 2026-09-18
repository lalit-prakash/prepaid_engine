using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A proposed tariff change moving through IT-draft → Utility-approval governance — the
/// workflow the pre-existing read-only <see cref="Tariff"/>/<see cref="TariffVersion"/> pair
/// never modeled (<see cref="TariffVersion"/> is a field-level audit trail, not a reproducible
/// snapshot; see its own doc comment). This entity carries a full proposed tariff configuration
/// (name, category, rates, and its own owned <see cref="ProposedSlabs"/>/
/// <see cref="ProposedTouPeriods"/> — the same value types <see cref="Tariff"/> itself owns, not
/// a parallel snapshot type) so it can reproduce the exact tariff that will go live.
///
/// <see cref="Tariff"/> is never edited in place (it never had an update method, and still
/// doesn't) — activating this request creates a brand-new <see cref="Tariff"/> row and retires
/// the superseded one (if any). Every historical bill's <c>TariffId</c> keeps pointing at an
/// immutable row with the exact rates it was billed under, so this workflow requires zero changes
/// to the billing calculation path itself.
///
/// Two roles move this through its lifecycle (<see cref="UserRole"/>): IT creates/edits/submits;
/// Utility approves (always with a commencement date, folding the spec's "Approved" and
/// "Scheduled" states into one transition — see <see cref="Approve"/>) or rejects (always with a
/// reason). The same actor is never allowed to both submit and approve the same request — see
/// <see cref="Approve"/>'s guard — on top of the primary defense of role-gating the approve/reject
/// endpoints entirely.
/// </summary>
public class TariffChangeRequest
{
    private readonly List<TariffSlab> _proposedSlabs = new();
    private readonly List<TouPeriod> _proposedTouPeriods = new();

    public Guid Id { get; private set; }

    /// <summary>The tariff this request revises, if any — null means this request proposes a
    /// brand-new tariff rather than a revision of an existing one.</summary>
    public Guid? SupersedesTariffId { get; private set; }

    /// <summary>Set once, when this request is <see cref="Activate"/>d — the new immutable
    /// <see cref="Tariff"/> row materialized from this request's proposed configuration.</summary>
    public Guid? ResultingTariffId { get; private set; }

    public string ProposedName { get; private set; }
    public ConsumerCategory ProposedCategory { get; private set; }
    public decimal ProposedFixedChargePerUnitPerMonth { get; private set; }
    public decimal ProposedPrepaidEnergyRebatePercent { get; private set; }
    public decimal ProposedEmergencyCreditLimit { get; private set; }
    public decimal? ProposedMinVendAmountSinglePhase { get; private set; }
    public decimal? ProposedMaxVendAmountSinglePhase { get; private set; }
    public decimal? ProposedMinVendAmountThreePhase { get; private set; }
    public decimal? ProposedMaxVendAmountThreePhase { get; private set; }
    public IReadOnlyCollection<TariffSlab> ProposedSlabs => _proposedSlabs.AsReadOnly();
    public IReadOnlyCollection<TouPeriod> ProposedTouPeriods => _proposedTouPeriods.AsReadOnly();

    public TariffChangeRequestStatus Status { get; private set; }

    public string CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string? ChangeReason { get; private set; }
    public string? SubmittedBy { get; private set; }
    public DateTime? SubmittedAt { get; private set; }

    public string? ApprovedBy { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? CommencementDate { get; private set; }

    public string? RejectedBy { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTime? ActivatedAt { get; private set; }

    public TariffChangeRequest(
        Guid id,
        Guid? supersedesTariffId,
        string proposedName,
        ConsumerCategory proposedCategory,
        IEnumerable<TariffSlab> proposedSlabs,
        decimal proposedFixedChargePerUnitPerMonth,
        decimal proposedPrepaidEnergyRebatePercent,
        decimal proposedEmergencyCreditLimit,
        string createdBy,
        DateTime createdAt,
        decimal? proposedMinVendAmountSinglePhase = null,
        decimal? proposedMaxVendAmountSinglePhase = null,
        decimal? proposedMinVendAmountThreePhase = null,
        decimal? proposedMaxVendAmountThreePhase = null,
        IEnumerable<TouPeriod>? proposedTouPeriods = null)
    {
        if (string.IsNullOrWhiteSpace(proposedName))
            throw new ArgumentException("A tariff name is required.", nameof(proposedName));
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new ArgumentException("The creating operator's identity is required.", nameof(createdBy));

        Id = id;
        SupersedesTariffId = supersedesTariffId;
        ProposedName = proposedName;
        ProposedCategory = proposedCategory;
        ProposedFixedChargePerUnitPerMonth = proposedFixedChargePerUnitPerMonth;
        ProposedPrepaidEnergyRebatePercent = proposedPrepaidEnergyRebatePercent;
        ProposedEmergencyCreditLimit = proposedEmergencyCreditLimit;
        ProposedMinVendAmountSinglePhase = proposedMinVendAmountSinglePhase;
        ProposedMaxVendAmountSinglePhase = proposedMaxVendAmountSinglePhase;
        ProposedMinVendAmountThreePhase = proposedMinVendAmountThreePhase;
        ProposedMaxVendAmountThreePhase = proposedMaxVendAmountThreePhase;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        Status = TariffChangeRequestStatus.Draft;

        _proposedSlabs.AddRange(proposedSlabs ?? throw new ArgumentNullException(nameof(proposedSlabs)));
        if (proposedTouPeriods is not null)
            _proposedTouPeriods.AddRange(proposedTouPeriods);
    }

    // EF Core / serialization
    private TariffChangeRequest()
    {
        ProposedName = string.Empty;
        CreatedBy = string.Empty;
    }

    /// <summary>Replaces the entire proposed configuration — only while this request is still
    /// editable (<see cref="Draft"/> or <see cref="Rejected"/>, per the spec's "IT can revise a
    /// rejected request" rule). Re-submitting after a rejection re-enters
    /// <see cref="PendingApproval"/> via <see cref="Submit"/>, not this method.</summary>
    public void UpdateProposal(
        string proposedName,
        ConsumerCategory proposedCategory,
        IEnumerable<TariffSlab> proposedSlabs,
        decimal proposedFixedChargePerUnitPerMonth,
        decimal proposedPrepaidEnergyRebatePercent,
        decimal proposedEmergencyCreditLimit,
        decimal? proposedMinVendAmountSinglePhase,
        decimal? proposedMaxVendAmountSinglePhase,
        decimal? proposedMinVendAmountThreePhase,
        decimal? proposedMaxVendAmountThreePhase,
        IEnumerable<TouPeriod>? proposedTouPeriods)
    {
        if (Status is not (TariffChangeRequestStatus.Draft or TariffChangeRequestStatus.Rejected))
            throw new InvalidOperationException($"Cannot edit a tariff change request that is {Status} — only Draft or Rejected requests can be edited.");
        if (string.IsNullOrWhiteSpace(proposedName))
            throw new ArgumentException("A tariff name is required.", nameof(proposedName));

        ProposedName = proposedName;
        ProposedCategory = proposedCategory;
        ProposedFixedChargePerUnitPerMonth = proposedFixedChargePerUnitPerMonth;
        ProposedPrepaidEnergyRebatePercent = proposedPrepaidEnergyRebatePercent;
        ProposedEmergencyCreditLimit = proposedEmergencyCreditLimit;
        ProposedMinVendAmountSinglePhase = proposedMinVendAmountSinglePhase;
        ProposedMaxVendAmountSinglePhase = proposedMaxVendAmountSinglePhase;
        ProposedMinVendAmountThreePhase = proposedMinVendAmountThreePhase;
        ProposedMaxVendAmountThreePhase = proposedMaxVendAmountThreePhase;

        _proposedSlabs.Clear();
        _proposedSlabs.AddRange(proposedSlabs ?? throw new ArgumentNullException(nameof(proposedSlabs)));
        _proposedTouPeriods.Clear();
        if (proposedTouPeriods is not null)
            _proposedTouPeriods.AddRange(proposedTouPeriods);

        // A revision after rejection starts a fresh review — its old rejection notes stay in the
        // audit trail but no longer describe the current proposal.
        if (Status == TariffChangeRequestStatus.Rejected)
        {
            Status = TariffChangeRequestStatus.Draft;
            RejectedBy = null;
            RejectedAt = null;
            RejectionReason = null;
        }
    }

    /// <summary>Field-level problems that must be fixed before this request can be submitted —
    /// spec's mandatory pre-submit validation (slab continuity/ordering, non-negative rates,
    /// non-overlapping ToD windows, valid vend ranges). Never throws; the caller decides how to
    /// present these.</summary>
    public IReadOnlyList<string> ValidateForSubmission()
    {
        var errors = new List<string>();

        if (_proposedSlabs.Count == 0 && _proposedTouPeriods.Count == 0)
            errors.Add("At least one energy slab or Time-of-Day period is required.");

        if (_proposedSlabs.Count > 0)
        {
            var ordered = _proposedSlabs.OrderBy(s => s.FromKwh).ToList();
            if (ordered[0].FromKwh != 0m)
                errors.Add("The first energy slab must start at 0 kWh.");

            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].RatePerKwh < 0)
                    errors.Add($"Slab starting at {ordered[i].FromKwh} kWh has a negative rate.");

                if (i < ordered.Count - 1)
                {
                    if (ordered[i].UpToKwh is null)
                        errors.Add($"Slab starting at {ordered[i].FromKwh} kWh is unbounded but is not the last slab.");
                    else if (ordered[i].UpToKwh!.Value != ordered[i + 1].FromKwh)
                        errors.Add($"Slab gap or overlap between {ordered[i].UpToKwh} kWh and {ordered[i + 1].FromKwh} kWh.");
                }
                else if (ordered[i].UpToKwh is not null)
                {
                    errors.Add("The last energy slab must be unbounded (no upper limit).");
                }
            }
        }

        if (_proposedTouPeriods.Count > 0)
        {
            var labels = new HashSet<string>();
            foreach (var period in _proposedTouPeriods)
            {
                if (period.RatePerKvah < 0)
                    errors.Add($"ToD period '{period.Label}' has a negative rate.");
                if (!labels.Add(period.Label))
                    errors.Add($"ToD period label '{period.Label}' is used more than once.");
            }

            for (var i = 0; i < _proposedTouPeriods.Count; i++)
            {
                for (var j = i + 1; j < _proposedTouPeriods.Count; j++)
                {
                    if (TouPeriodsOverlap(_proposedTouPeriods[i], _proposedTouPeriods[j]))
                        errors.Add($"ToD periods '{_proposedTouPeriods[i].Label}' and '{_proposedTouPeriods[j].Label}' overlap.");
                }
            }
        }

        if (ProposedMinVendAmountSinglePhase.HasValue && ProposedMaxVendAmountSinglePhase.HasValue
            && ProposedMinVendAmountSinglePhase.Value > ProposedMaxVendAmountSinglePhase.Value)
        {
            errors.Add("Minimum single-phase vend amount cannot exceed the maximum.");
        }
        if (ProposedMinVendAmountThreePhase.HasValue && ProposedMaxVendAmountThreePhase.HasValue
            && ProposedMinVendAmountThreePhase.Value > ProposedMaxVendAmountThreePhase.Value)
        {
            errors.Add("Minimum three-phase vend amount cannot exceed the maximum.");
        }
        if (ProposedPrepaidEnergyRebatePercent is < 0 or > 100)
            errors.Add("Prepaid energy rebate must be between 0 and 100 percent.");
        if (ProposedEmergencyCreditLimit < 0)
            errors.Add("Emergency credit limit cannot be negative.");

        return errors;
    }

    private static bool TouPeriodsOverlap(TouPeriod a, TouPeriod b)
    {
        // Sample every minute boundary of a's span (including wrap-past-midnight) against b's
        // Contains() — reuses the same wrap-aware logic Tariff.ClassifyTimeOfDay depends on,
        // rather than re-deriving interval-overlap arithmetic that would need to special-case
        // midnight wraparound a second time.
        var startMinutes = (int)a.StartTime.TotalMinutes;
        var span = a.EndTime > a.StartTime ? a.EndTime - a.StartTime : TimeSpan.FromDays(1) - a.StartTime + a.EndTime;
        var steps = (int)span.TotalMinutes;
        const int minutesPerDay = 24 * 60;
        for (var i = 0; i < steps; i++)
        {
            var t = TimeSpan.FromMinutes((startMinutes + i) % minutesPerDay);
            if (b.Contains(t))
                return true;
        }
        return false;
    }

    public void Submit(string submittedBy, string changeReason, DateTime submittedAt)
    {
        if (Status is not (TariffChangeRequestStatus.Draft or TariffChangeRequestStatus.Rejected))
            throw new InvalidOperationException($"Cannot submit a tariff change request that is {Status} — only Draft or Rejected requests can be submitted.");
        if (string.IsNullOrWhiteSpace(submittedBy))
            throw new ArgumentException("The submitting operator's identity is required.", nameof(submittedBy));
        if (string.IsNullOrWhiteSpace(changeReason))
            throw new ArgumentException("A change reason is required to submit a tariff change request.", nameof(changeReason));

        var validationErrors = ValidateForSubmission();
        if (validationErrors.Count > 0)
            throw new InvalidOperationException("Cannot submit an invalid tariff change request: " + string.Join(" ", validationErrors));

        Status = TariffChangeRequestStatus.PendingApproval;
        SubmittedBy = submittedBy;
        ChangeReason = changeReason;
        SubmittedAt = submittedAt;
    }

    /// <summary>Approves and schedules this request in one step — the spec's "Approved" and
    /// "Scheduled" states are the same real-world moment here (a Utility approval always names a
    /// commencement date). Rejects the same actor approving their own submission as a second,
    /// defense-in-depth check on top of role-gating the endpoint itself.</summary>
    public void Approve(string approvedBy, DateTime commencementDate, DateTime approvedAt)
    {
        if (Status != TariffChangeRequestStatus.PendingApproval)
            throw new InvalidOperationException($"Cannot approve a tariff change request that is {Status} — only a PendingApproval request can be approved.");
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("The approving operator's identity is required.", nameof(approvedBy));
        if (string.Equals(approvedBy, SubmittedBy, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The operator who submitted this request cannot also approve it.");
        if (commencementDate.Date < approvedAt.Date)
            throw new ArgumentOutOfRangeException(nameof(commencementDate), "Commencement date cannot be before the approval date.");

        Status = TariffChangeRequestStatus.Scheduled;
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        CommencementDate = commencementDate;
    }

    public void Reject(string rejectedBy, string rejectionReason, DateTime rejectedAt)
    {
        if (Status != TariffChangeRequestStatus.PendingApproval)
            throw new InvalidOperationException($"Cannot reject a tariff change request that is {Status} — only a PendingApproval request can be rejected.");
        if (string.IsNullOrWhiteSpace(rejectionReason))
            throw new ArgumentException("A rejection reason is required.", nameof(rejectionReason));

        Status = TariffChangeRequestStatus.Rejected;
        RejectedBy = rejectedBy;
        RejectionReason = rejectionReason;
        RejectedAt = rejectedAt;
    }

    public void Cancel()
    {
        if (Status is TariffChangeRequestStatus.Activated or TariffChangeRequestStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel a tariff change request that is {Status}.");

        Status = TariffChangeRequestStatus.Cancelled;
    }

    /// <summary>Marks this request Activated once the caller has already materialized
    /// <paramref name="resultingTariffId"/> and retired the superseded tariff (if any) — mirrors
    /// this project's established "decision-trail entity never itself touches the real state"
    /// pattern (see <c>ConversionRequest.Complete</c>). Only valid at/after
    /// <see cref="CommencementDate"/>, and only once.</summary>
    public void Activate(Guid resultingTariffId, DateTime activatedAt)
    {
        if (Status != TariffChangeRequestStatus.Scheduled)
            throw new InvalidOperationException($"Cannot activate a tariff change request that is {Status} — only a Scheduled request can be activated.");
        if (activatedAt.Date < CommencementDate!.Value.Date)
            throw new InvalidOperationException($"Cannot activate before the commencement date ({CommencementDate:d}).");

        Status = TariffChangeRequestStatus.Activated;
        ResultingTariffId = resultingTariffId;
        ActivatedAt = activatedAt;
    }
}
