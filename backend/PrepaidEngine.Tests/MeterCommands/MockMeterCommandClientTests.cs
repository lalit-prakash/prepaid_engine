using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Infrastructure.MeterCommands;
using Xunit;

namespace PrepaidEngine.Tests.MeterCommands;

public class MockMeterCommandClientTests
{
    private readonly MockMeterCommandClient _client = new();

    [Fact]
    public async Task SendCreditCommandAsync_DefaultCorrelationId_ReturnsAcknowledged()
    {
        var result = await _client.SendCreditCommandAsync(
            new SendCreditCommandRequest(Guid.NewGuid(), 300m, "ui-1234567890"));

        Assert.Equal(MeterCommandOutcome.Acknowledged, result.Outcome);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData("METERFAIL-abc")]
    [InlineData("prefix-METERFAIL-suffix")]
    [InlineData("meterfail-lowercase")]
    public async Task SendCreditCommandAsync_CorrelationIdContainsMeterFail_ReturnsFailed(string correlationId)
    {
        var result = await _client.SendCreditCommandAsync(
            new SendCreditCommandRequest(Guid.NewGuid(), 300m, correlationId));

        Assert.Equal(MeterCommandOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Message);
    }

    [Theory]
    [InlineData("METERTIMEOUT-abc")]
    [InlineData("prefix-METERTIMEOUT-suffix")]
    public async Task SendCreditCommandAsync_CorrelationIdContainsMeterTimeout_ReturnsTimedOut(string correlationId)
    {
        var result = await _client.SendCreditCommandAsync(
            new SendCreditCommandRequest(Guid.NewGuid(), 300m, correlationId));

        Assert.Equal(MeterCommandOutcome.TimedOut, result.Outcome);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task SendCreditCommandAsync_NonPositiveCreditAmount_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _client.SendCreditCommandAsync(new SendCreditCommandRequest(Guid.NewGuid(), 0m, "abc")));
    }

    [Fact]
    public async Task SendCreditCommandAsync_NullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _client.SendCreditCommandAsync(null!));
    }

    [Fact]
    public async Task SendCreditCommandAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _client.SendCreditCommandAsync(
                new SendCreditCommandRequest(Guid.NewGuid(), 300m, "abc"), cts.Token));
    }
}
