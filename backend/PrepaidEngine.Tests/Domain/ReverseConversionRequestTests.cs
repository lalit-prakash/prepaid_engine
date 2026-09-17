using System;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class ReverseConversionRequestTests
{
    [Fact]
    public void Constructor_SetsRequestedStatus()
    {
        var request = new ReverseConversionRequest(Guid.NewGuid(), Guid.NewGuid(), "operator1", "Consumer requested postpaid billing.", DateTime.UtcNow);

        Assert.Equal(ReverseConversionStatus.Requested, request.Status);
    }

    [Fact]
    public void Constructor_EmptyReason_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ReverseConversionRequest(Guid.NewGuid(), Guid.NewGuid(), "operator1", "", DateTime.UtcNow));
    }

    [Fact]
    public void Constructor_EmptyRequestedBy_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ReverseConversionRequest(Guid.NewGuid(), Guid.NewGuid(), "", "reason", DateTime.UtcNow));
    }

    [Fact]
    public void Complete_SetsFinalReadingsAndStatus()
    {
        var request = new ReverseConversionRequest(Guid.NewGuid(), Guid.NewGuid(), "operator1", "reason", DateTime.UtcNow);

        request.Complete(1234.5m, 850.25m, DateTime.UtcNow);

        Assert.Equal(ReverseConversionStatus.Completed, request.Status);
        Assert.Equal(1234.5m, request.FinalMeterReadingKwh);
        Assert.Equal(850.25m, request.FinalWalletBalance);
    }

    [Fact]
    public void Complete_AlreadyCompleted_Throws()
    {
        var request = new ReverseConversionRequest(Guid.NewGuid(), Guid.NewGuid(), "operator1", "reason", DateTime.UtcNow);
        request.Complete(100m, 50m, DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => request.Complete(200m, 60m, DateTime.UtcNow));
    }

    [Fact]
    public void Reject_WithoutNote_Throws()
    {
        var request = new ReverseConversionRequest(Guid.NewGuid(), Guid.NewGuid(), "operator1", "reason", DateTime.UtcNow);

        Assert.Throws<ArgumentException>(() => request.Reject("", DateTime.UtcNow));
    }

    [Fact]
    public void Reject_AfterCompleted_Throws()
    {
        var request = new ReverseConversionRequest(Guid.NewGuid(), Guid.NewGuid(), "operator1", "reason", DateTime.UtcNow);
        request.Complete(100m, 50m, DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => request.Reject("too late", DateTime.UtcNow));
    }
}
