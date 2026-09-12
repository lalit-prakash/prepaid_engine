using System;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class DailyLoadProfileTests
{
    private static DailyLoadProfile NewProfile(bool provisional = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 10), DateTime.UtcNow,
            1000m, 1042.5m, isProvisional: provisional);

    [Fact]
    public void Constructor_ComputesTotalKwh()
    {
        var profile = NewProfile();

        Assert.Equal(42.5m, profile.TotalKwh);
    }

    [Fact]
    public void Constructor_EndBelowStart_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DailyLoadProfile(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 10), DateTime.UtcNow, 100m, 50m));
    }

    [Fact]
    public void CreateProvisional_SetsIsProvisionalAndProvisionalStatus()
    {
        var profile = DailyLoadProfile.CreateProvisional(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 10), DateTime.UtcNow, 18m);

        Assert.True(profile.IsProvisional);
        Assert.Equal(DailyProfileStatus.Provisional, profile.Status);
        Assert.Equal(18m, profile.TotalKwh);
    }

    [Fact]
    public void MarkValidated_OnProvisionalProfile_Throws()
    {
        var profile = NewProfile(provisional: true);

        Assert.Throws<InvalidOperationException>(() => profile.MarkValidated());
    }

    [Fact]
    public void ReplaceWithActual_OnNonProvisionalProfile_Throws()
    {
        var profile = NewProfile(provisional: false);

        Assert.Throws<InvalidOperationException>(() => profile.ReplaceWithActual(0m, 20m, DateTime.UtcNow, null));
    }

    [Fact]
    public void ReplaceWithActual_OnProvisionalProfile_UpdatesDataAndClearsProvisionalFlag()
    {
        var profile = NewProfile(provisional: true);

        profile.ReplaceWithActual(1000m, 1050m, DateTime.UtcNow, "HES-DLP-1");

        Assert.False(profile.IsProvisional);
        Assert.Equal(50m, profile.TotalKwh);
        Assert.Equal(DailyProfileStatus.Validated, profile.Status);
    }
}
