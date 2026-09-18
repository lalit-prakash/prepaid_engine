using System;
using System.Linq;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class TariffChangeRequestTests
{
    private static TariffSlab[] ValidSlabs() =>
        new[] { new TariffSlab(0, 100, 5.00m), new TariffSlab(100, null, 5.10m) };

    private static TariffChangeRequest NewDraft(Guid? supersedes = null) =>
        new(Guid.NewGuid(), supersedes, "DLT Revised", ConsumerCategory.Domestic, ValidSlabs(),
            proposedFixedChargePerUnitPerMonth: 90m, proposedPrepaidEnergyRebatePercent: 2m, proposedEmergencyCreditLimit: 200m,
            createdBy: "it_user", createdAt: DateTime.UtcNow);

    [Fact]
    public void Constructor_StartsInDraft()
    {
        var request = NewDraft();
        Assert.Equal(TariffChangeRequestStatus.Draft, request.Status);
    }

    [Fact]
    public void Constructor_EmptyCreatedBy_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TariffChangeRequest(
            Guid.NewGuid(), null, "Name", ConsumerCategory.Domestic, ValidSlabs(), 0m, 0m, 0m, "", DateTime.UtcNow));
    }

    [Fact]
    public void ValidateForSubmission_ContinuousSlabsStartingAtZero_NoErrors()
    {
        var request = NewDraft();
        Assert.Empty(request.ValidateForSubmission());
    }

    [Fact]
    public void ValidateForSubmission_FirstSlabNotAtZero_ReportsError()
    {
        var request = new TariffChangeRequest(Guid.NewGuid(), null, "Bad", ConsumerCategory.Domestic,
            new[] { new TariffSlab(10, null, 5m) }, 0m, 0m, 0m, "it_user", DateTime.UtcNow);

        Assert.Contains(request.ValidateForSubmission(), e => e.Contains("must start at 0"));
    }

    [Fact]
    public void ValidateForSubmission_SlabGap_ReportsError()
    {
        var request = new TariffChangeRequest(Guid.NewGuid(), null, "Bad", ConsumerCategory.Domestic,
            new[] { new TariffSlab(0, 100, 5m), new TariffSlab(150, null, 6m) }, 0m, 0m, 0m, "it_user", DateTime.UtcNow);

        Assert.Contains(request.ValidateForSubmission(), e => e.Contains("gap or overlap"));
    }

    [Fact]
    public void ValidateForSubmission_LastSlabBounded_ReportsError()
    {
        var request = new TariffChangeRequest(Guid.NewGuid(), null, "Bad", ConsumerCategory.Domestic,
            new[] { new TariffSlab(0, 100, 5m) }, 0m, 0m, 0m, "it_user", DateTime.UtcNow);

        Assert.Contains(request.ValidateForSubmission(), e => e.Contains("must be unbounded"));
    }

    [Fact]
    public void ValidateForSubmission_OverlappingTouPeriods_ReportsError()
    {
        var request = new TariffChangeRequest(Guid.NewGuid(), null, "ToD Tariff", ConsumerCategory.Industrial,
            Array.Empty<TariffSlab>(), 0m, 0m, 0m, "it_user", DateTime.UtcNow,
            proposedTouPeriods: new[]
            {
                new TouPeriod("Normal", TimeSpan.FromHours(6), TimeSpan.FromHours(18), 5.55m),
                new TouPeriod("Peak", TimeSpan.FromHours(17), TimeSpan.FromHours(23), 6.66m),
            });

        Assert.Contains(request.ValidateForSubmission(), e => e.Contains("overlap"));
    }

    [Fact]
    public void ValidateForSubmission_NonOverlappingTouPeriods_NoErrors()
    {
        var request = new TariffChangeRequest(Guid.NewGuid(), null, "ToD Tariff", ConsumerCategory.Industrial,
            Array.Empty<TariffSlab>(), 0m, 0m, 0m, "it_user", DateTime.UtcNow,
            proposedTouPeriods: new[]
            {
                new TouPeriod("Normal", TimeSpan.FromHours(6), TimeSpan.FromHours(17), 5.55m),
                new TouPeriod("Peak", TimeSpan.FromHours(17), TimeSpan.FromHours(23), 6.66m),
                new TouPeriod("Off-Peak", TimeSpan.FromHours(23), TimeSpan.FromHours(6), 4.72m),
            });

        Assert.Empty(request.ValidateForSubmission());
    }

    [Fact]
    public void Submit_ValidRequest_MovesToPendingApproval()
    {
        var request = NewDraft();
        request.Submit("it_user", "Annual tariff revision", DateTime.UtcNow);

        Assert.Equal(TariffChangeRequestStatus.PendingApproval, request.Status);
        Assert.Equal("Annual tariff revision", request.ChangeReason);
    }

    [Fact]
    public void Submit_InvalidRequest_Throws()
    {
        var request = new TariffChangeRequest(Guid.NewGuid(), null, "Bad", ConsumerCategory.Domestic,
            new[] { new TariffSlab(10, null, 5m) }, 0m, 0m, 0m, "it_user", DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => request.Submit("it_user", "reason", DateTime.UtcNow));
    }

    [Fact]
    public void Submit_EmptyChangeReason_Throws()
    {
        var request = NewDraft();
        Assert.Throws<ArgumentException>(() => request.Submit("it_user", "", DateTime.UtcNow));
    }

    [Fact]
    public void Approve_ByDifferentActor_SchedulesWithCommencementDate()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);

        request.Approve("utility_user", DateTime.UtcNow.AddDays(14), DateTime.UtcNow);

        Assert.Equal(TariffChangeRequestStatus.Scheduled, request.Status);
        Assert.NotNull(request.CommencementDate);
    }

    [Fact]
    public void Approve_SameActorAsSubmitter_ThrowsSelfApprovalPrevention()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => request.Approve("it_user", DateTime.UtcNow.AddDays(1), DateTime.UtcNow));
    }

    [Fact]
    public void Approve_CommencementBeforeApprovalDate_Throws()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            request.Approve("utility_user", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow));
    }

    [Fact]
    public void Approve_NotPendingApproval_Throws()
    {
        var request = NewDraft();
        Assert.Throws<InvalidOperationException>(() => request.Approve("utility_user", DateTime.UtcNow, DateTime.UtcNow));
    }

    [Fact]
    public void Reject_RequiresReason()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);

        Assert.Throws<ArgumentException>(() => request.Reject("utility_user", "", DateTime.UtcNow));
    }

    [Fact]
    public void Reject_ThenRevise_ReturnsToDraftAndClearsRejection()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);
        request.Reject("utility_user", "Rates too high", DateTime.UtcNow);
        Assert.Equal(TariffChangeRequestStatus.Rejected, request.Status);

        request.UpdateProposal("DLT Revised v2", ConsumerCategory.Domestic, ValidSlabs(), 85m, 2m, 200m, null, null, null, null, null);

        Assert.Equal(TariffChangeRequestStatus.Draft, request.Status);
        Assert.Null(request.RejectionReason);
    }

    [Fact]
    public void Reject_ThenResubmit_ReenterPendingApproval()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);
        request.Reject("utility_user", "Rates too high", DateTime.UtcNow);

        request.Submit("it_user", "Revised per feedback", DateTime.UtcNow);

        Assert.Equal(TariffChangeRequestStatus.PendingApproval, request.Status);
    }

    [Fact]
    public void UpdateProposal_WhenPendingApproval_Throws()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            request.UpdateProposal("New", ConsumerCategory.Domestic, ValidSlabs(), 1m, 1m, 1m, null, null, null, null, null));
    }

    [Fact]
    public void Activate_BeforeCommencementDate_Throws()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);
        request.Approve("utility_user", DateTime.UtcNow.Date.AddDays(10), DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => request.Activate(Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public void Activate_AtOrAfterCommencementDate_Succeeds()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);
        var commencement = DateTime.UtcNow.Date;
        request.Approve("utility_user", commencement, DateTime.UtcNow);

        var resultingTariffId = Guid.NewGuid();
        request.Activate(resultingTariffId, commencement);

        Assert.Equal(TariffChangeRequestStatus.Activated, request.Status);
        Assert.Equal(resultingTariffId, request.ResultingTariffId);
    }

    [Fact]
    public void Activate_Twice_Throws()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);
        var commencement = DateTime.UtcNow.Date;
        request.Approve("utility_user", commencement, DateTime.UtcNow);
        request.Activate(Guid.NewGuid(), commencement);

        Assert.Throws<InvalidOperationException>(() => request.Activate(Guid.NewGuid(), commencement));
    }

    [Fact]
    public void Cancel_FromDraft_Succeeds()
    {
        var request = NewDraft();
        request.Cancel();
        Assert.Equal(TariffChangeRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public void Cancel_AlreadyActivated_Throws()
    {
        var request = NewDraft();
        request.Submit("it_user", "reason", DateTime.UtcNow);
        var commencement = DateTime.UtcNow.Date;
        request.Approve("utility_user", commencement, DateTime.UtcNow);
        request.Activate(Guid.NewGuid(), commencement);

        Assert.Throws<InvalidOperationException>(() => request.Cancel());
    }
}
