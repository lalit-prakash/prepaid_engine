using System;
using PrepaidEngine.Domain.Entities;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class MeterBillingControlTests
{
    [Fact]
    public void Constructor_StartsBlocked()
    {
        var control = new MeterBillingControl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "negative consumption", DateTime.UtcNow);

        Assert.True(control.ActualBillingBlocked);
        Assert.Null(control.ClearedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingReason_Throws(string? reason)
    {
        Assert.Throws<ArgumentException>(() => new MeterBillingControl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), reason!, DateTime.UtcNow));
    }

    [Fact]
    public void Clear_WhenBlocked_Succeeds()
    {
        var control = new MeterBillingControl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "negative consumption", DateTime.UtcNow);

        control.Clear(DateTime.UtcNow);

        Assert.False(control.ActualBillingBlocked);
        Assert.NotNull(control.ClearedAt);
    }

    [Fact]
    public void Clear_WhenNotBlocked_Throws()
    {
        var control = new MeterBillingControl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "negative consumption", DateTime.UtcNow);
        control.Clear(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => control.Clear(DateTime.UtcNow));
    }

    [Fact]
    public void Reactivate_AfterClear_SetsBlockedAgainWithNewReason()
    {
        var control = new MeterBillingControl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "first reason", DateTime.UtcNow);
        control.Clear(DateTime.UtcNow);

        control.Reactivate("second reason", DateTime.UtcNow);

        Assert.True(control.ActualBillingBlocked);
        Assert.Equal("second reason", control.BlockReason);
        Assert.Null(control.ClearedAt);
    }
}
