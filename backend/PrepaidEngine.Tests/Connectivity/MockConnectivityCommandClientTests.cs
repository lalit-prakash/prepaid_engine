using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Connectivity;
using Xunit;

namespace PrepaidEngine.Tests.Connectivity;

public class MockConnectivityCommandClientTests
{
    private readonly MockConnectivityCommandClient _client = new();

    [Fact]
    public async Task SendConnectivityCommandAsync_DefaultCorrelationId_ReturnsAcknowledged()
    {
        var result = await _client.SendConnectivityCommandAsync(
            new SendConnectivityCommandRequest(Guid.NewGuid(), ConnectivityCommandType.Disconnect, "ui-1234567890"));

        Assert.Equal(ConnectivityCommandOutcome.Acknowledged, result.Outcome);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData("CONNFAIL-abc")]
    [InlineData("prefix-CONNFAIL-suffix")]
    [InlineData("connfail-lowercase")]
    public async Task SendConnectivityCommandAsync_CorrelationIdContainsConnFail_ReturnsFailed(string correlationId)
    {
        var result = await _client.SendConnectivityCommandAsync(
            new SendConnectivityCommandRequest(Guid.NewGuid(), ConnectivityCommandType.Disconnect, correlationId));

        Assert.Equal(ConnectivityCommandOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Message);
    }

    [Theory]
    [InlineData("CONNTIMEOUT-abc")]
    [InlineData("prefix-CONNTIMEOUT-suffix")]
    public async Task SendConnectivityCommandAsync_CorrelationIdContainsConnTimeout_ReturnsTimedOut(string correlationId)
    {
        var result = await _client.SendConnectivityCommandAsync(
            new SendConnectivityCommandRequest(Guid.NewGuid(), ConnectivityCommandType.Reconnect, correlationId));

        Assert.Equal(ConnectivityCommandOutcome.TimedOut, result.Outcome);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task SendConnectivityCommandAsync_NullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _client.SendConnectivityCommandAsync(null!));
    }

    [Fact]
    public async Task SendConnectivityCommandAsync_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _client.SendConnectivityCommandAsync(
                new SendConnectivityCommandRequest(Guid.NewGuid(), ConnectivityCommandType.Disconnect, "abc"), cts.Token));
    }
}
