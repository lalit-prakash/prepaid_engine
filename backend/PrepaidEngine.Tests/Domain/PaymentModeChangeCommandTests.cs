using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class PaymentModeChangeCommandTests
{
    private static PaymentModeChangeCommand NewCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

    [Fact]
    public void Constructor_StartsInQueuedStatus()
    {
        var command = NewCommand();

        Assert.Equal(PaymentModeChangeStatus.Queued, command.Status);
        Assert.Null(command.SentAt);
        Assert.Null(command.AcknowledgedAt);
        Assert.Null(command.ErrorMessage);
        Assert.Null(command.MeterReadingAtConversion);
    }

    [Fact]
    public void MarkSent_FromQueued_TransitionsToSent()
    {
        var command = NewCommand();
        var sentAt = DateTime.UtcNow;

        command.MarkSent(sentAt);

        Assert.Equal(PaymentModeChangeStatus.Sent, command.Status);
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
    public void MarkAcknowledged_FromSent_RecordsReadingAndTransitions()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);
        var ackAt = DateTime.UtcNow;

        command.MarkAcknowledged(ackAt, 1250.5m);

        Assert.Equal(PaymentModeChangeStatus.Acknowledged, command.Status);
        Assert.Equal(ackAt, command.AcknowledgedAt);
        Assert.Equal(1250.5m, command.MeterReadingAtConversion);
    }

    [Fact]
    public void MarkAcknowledged_WithoutBeingSentFirst_Throws()
    {
        var command = NewCommand();

        Assert.Throws<InvalidOperationException>(() => command.MarkAcknowledged(DateTime.UtcNow, 100m));
    }

    [Fact]
    public void MarkAcknowledged_NegativeReading_Throws()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() => command.MarkAcknowledged(DateTime.UtcNow, -1m));
    }

    [Fact]
    public void MarkFailed_FromSent_TransitionsToFailed()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);

        command.MarkFailed("meter/HES rejected the command");

        Assert.Equal(PaymentModeChangeStatus.Failed, command.Status);
        Assert.Equal("meter/HES rejected the command", command.ErrorMessage);
    }

    [Fact]
    public void MarkFailed_WhenAlreadyAcknowledged_Throws()
    {
        var command = NewCommand();
        command.MarkSent(DateTime.UtcNow);
        command.MarkAcknowledged(DateTime.UtcNow, 100m);

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

        Assert.Equal(PaymentModeChangeStatus.TimedOut, command.Status);
    }

    [Fact]
    public void MarkTimedOut_WithoutBeingSentFirst_Throws()
    {
        var command = NewCommand();

        Assert.Throws<InvalidOperationException>(() => command.MarkTimedOut());
    }
}
