using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class ConnectivityCommandTests
{
    private static ConnectivityCommand NewCommand(
        ConnectivityCommandType type = ConnectivityCommandType.Disconnect,
        string reason = "Negative credit") =>
        new(Guid.NewGuid(), Guid.NewGuid(), type, reason, DateTime.UtcNow);

    [Fact]
    public void Constructor_StartsInQueuedStatusWithZeroRetries()
    {
        var command = NewCommand();

        Assert.Equal(ConnectivityCommandStatus.Queued, command.Status);
        Assert.Equal(0, command.RetryCount);
        Assert.Null(command.SentAt);
        Assert.Null(command.AcknowledgedAt);
        Assert.Null(command.ErrorMessage);
    }

    [Theory]
    [InlineData(ConnectivityCommandType.Disconnect)]
    [InlineData(ConnectivityCommandType.Reconnect)]
    public void Constructor_RecordsTheRequestedCommandType(ConnectivityCommandType type)
    {
        var command = NewCommand(type);

        Assert.Equal(type, command.CommandType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_MissingReason_Throws(string? reason)
    {
        Assert.Throws<ArgumentException>(
            () => new ConnectivityCommand(Guid.NewGuid(), Guid.NewGuid(), ConnectivityCommandType.Disconnect, reason!, DateTime.UtcNow));
    }

    [Fact]
    public void MarkSent_FromQueued_TransitionsToSent()
    {
        var command = NewCommand();
        var sentAt = DateTime.UtcNow;

        command.MarkSent(sentAt);

        Assert.Equal(ConnectivityCommandStatus.Sent, command.Status);
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

        Assert.Equal(ConnectivityCommandStatus.Acknowledged, command.Status);
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

        Assert.Equal(ConnectivityCommandStatus.Failed, command.Status);
        Assert.Equal("meter unreachable", command.ErrorMessage);
    }

    [Fact]
    public void MarkFailed_FromSent_TransitionsToFailed()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);

        command.MarkFailed("negative acknowledgement from meter");

        Assert.Equal(ConnectivityCommandStatus.Failed, command.Status);
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

        Assert.Equal(ConnectivityCommandStatus.TimedOut, command.Status);
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

        Assert.Equal(ConnectivityCommandStatus.Queued, command.Status);
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

        Assert.Equal(ConnectivityCommandStatus.Queued, command.Status);
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
        Assert.Equal(ConnectivityCommandStatus.Queued, command.Status);
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
