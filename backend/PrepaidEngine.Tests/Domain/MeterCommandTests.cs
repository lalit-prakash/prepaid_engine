using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class MeterCommandTests
{
    private static MeterCommand NewCommand(decimal creditAmount = 300m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), creditAmount, DateTime.UtcNow);

    [Fact]
    public void Constructor_StartsInQueuedStatusWithZeroRetries()
    {
        var command = NewCommand();

        Assert.Equal(MeterCommandStatus.Queued, command.Status);
        Assert.Equal(0, command.RetryCount);
        Assert.Null(command.SentAt);
        Assert.Null(command.AcknowledgedAt);
        Assert.Null(command.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void Constructor_NonPositiveCreditAmount_Throws(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MeterCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, DateTime.UtcNow));
    }

    [Fact]
    public void MarkSent_FromQueued_TransitionsToSent()
    {
        var command = NewCommand();
        var sentAt = DateTime.UtcNow;

        command.MarkSent(sentAt);

        Assert.Equal(MeterCommandStatus.Sent, command.Status);
        Assert.Equal(sentAt, command.SentAt);
    }

    [Fact]
    public void MarkSent_WhenNotQueued_Throws()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => command.MarkSent(DateTime.UtcNow));
    }

    [Fact]
    public void MarkAcknowledged_FromSent_TransitionsToAcknowledged()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);
        var ackAt = DateTime.UtcNow;

        command.MarkAcknowledged(ackAt);

        Assert.Equal(MeterCommandStatus.Acknowledged, command.Status);
        Assert.Equal(ackAt, command.AcknowledgedAt);
    }

    [Fact]
    public void MarkAcknowledged_WithoutBeingSentFirst_Throws()
    {
        var command = NewCommand();

        Assert.Throws<InvalidOperationException>(() => command.MarkAcknowledged(DateTime.UtcNow));
    }

    [Fact]
    public void MarkFailed_FromQueued_TransitionsToFailed()
    {
        var command = NewCommand();

        command.MarkFailed("meter unreachable");

        Assert.Equal(MeterCommandStatus.Failed, command.Status);
        Assert.Equal("meter unreachable", command.ErrorMessage);
    }

    [Fact]
    public void MarkFailed_FromSent_TransitionsToFailed()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);

        command.MarkFailed("negative acknowledgement from meter");

        Assert.Equal(MeterCommandStatus.Failed, command.Status);
    }

    [Fact]
    public void MarkFailed_WhenAlreadyAcknowledged_Throws()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);
        command.MarkAcknowledged(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => command.MarkFailed("too late"));
    }

    [Fact]
    public void MarkFailed_WithoutErrorMessage_Throws()
    {
        var command = NewCommand();

        Assert.Throws<ArgumentException>(() => command.MarkFailed(""));
    }

    [Fact]
    public void MarkTimedOut_FromSent_TransitionsToTimedOut()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);

        command.MarkTimedOut();

        Assert.Equal(MeterCommandStatus.TimedOut, command.Status);
    }

    [Fact]
    public void MarkTimedOut_WithoutBeingSentFirst_Throws()
    {
        var command = NewCommand();

        Assert.Throws<InvalidOperationException>(() => command.MarkTimedOut());
    }

    [Fact]
    public void Retry_FromFailed_ResetsToQueuedAndIncrementsRetryCount()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);
        command.MarkFailed("timeout at meter gateway");

        command.Retry();

        Assert.Equal(MeterCommandStatus.Queued, command.Status);
        Assert.Equal(1, command.RetryCount);
        Assert.Null(command.ErrorMessage);
        Assert.Null(command.SentAt);
    }

    [Fact]
    public void Retry_FromTimedOut_ResetsToQueuedAndIncrementsRetryCount()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);
        command.MarkTimedOut();

        command.Retry();

        Assert.Equal(MeterCommandStatus.Queued, command.Status);
        Assert.Equal(1, command.RetryCount);
    }

    [Fact]
    public void Retry_MultipleTimes_AccumulatesRetryCount()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);
        command.MarkFailed("first failure");
        command.Retry();
        command.MarkSent(DateTime.UtcNow);
        command.MarkFailed("second failure");
        command.Retry();

        Assert.Equal(2, command.RetryCount);
        Assert.Equal(MeterCommandStatus.Queued, command.Status);
    }

    [Fact]
    public void Retry_WhenNotFailedOrTimedOut_Throws()
    {
        var command = NewCommand();

        Assert.Throws<InvalidOperationException>(() => command.Retry());
    }

    [Fact]
    public void Retry_WhenAcknowledged_Throws()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);
        command.MarkAcknowledged(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => command.Retry());
    }
}
