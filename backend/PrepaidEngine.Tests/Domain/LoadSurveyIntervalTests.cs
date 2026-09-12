using System;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class LoadSurveyIntervalTests
{
    private static LoadSurveyInterval NewInterval(DateTime? start = null, DateTime? end = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            start ?? new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc),
            end ?? new DateTime(2026, 9, 11, 10, 30, 0, DateTimeKind.Utc),
            cumulativeKwh: 100m, intervalKwh: 1.2m, receivedAt: DateTime.UtcNow);

    [Fact]
    public void Constructor_StartsAsReceivedAndValid()
    {
        var interval = NewInterval();

        Assert.Equal(LoadSurveyStatus.Received, interval.Status);
        Assert.Equal(LoadSurveyQuality.Valid, interval.Quality);
    }

    [Fact]
    public void Constructor_NotExactly30Minutes_Throws()
    {
        var start = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => NewInterval(start, start.AddMinutes(45)));
    }

    [Fact]
    public void Constructor_NegativeCumulative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoadSurveyInterval(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 11, 10, 30, 0, DateTimeKind.Utc),
            cumulativeKwh: -1m, intervalKwh: 1m, receivedAt: DateTime.UtcNow));
    }

    [Fact]
    public void MarkValidated_SetsValidatedStatusAndValidQuality()
    {
        var interval = NewInterval();

        interval.MarkValidated();

        Assert.Equal(LoadSurveyStatus.Validated, interval.Status);
        Assert.Equal(LoadSurveyQuality.Valid, interval.Quality);
    }

    [Fact]
    public void MarkRejected_WithValidQuality_Throws()
    {
        var interval = NewInterval();

        Assert.Throws<ArgumentException>(() => interval.MarkRejected(LoadSurveyQuality.Valid));
    }

    [Fact]
    public void MarkRejected_WithNegativeConsumption_SetsRejectedStatus()
    {
        var interval = NewInterval();

        interval.MarkRejected(LoadSurveyQuality.NegativeConsumption);

        Assert.Equal(LoadSurveyStatus.Rejected, interval.Status);
        Assert.Equal(LoadSurveyQuality.NegativeConsumption, interval.Quality);
    }

    [Fact]
    public void MarkProcessed_WhenNotValidated_Throws()
    {
        var interval = NewInterval();

        Assert.Throws<InvalidOperationException>(() => interval.MarkProcessed());
    }

    [Fact]
    public void MarkProcessed_AfterValidated_Succeeds()
    {
        var interval = NewInterval();
        interval.MarkValidated();

        interval.MarkProcessed();

        Assert.Equal(LoadSurveyStatus.Processed, interval.Status);
    }
}
