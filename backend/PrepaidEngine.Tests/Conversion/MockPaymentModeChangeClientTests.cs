using PrepaidEngine.Application.Conversion;
using PrepaidEngine.Infrastructure.Conversion;
using Xunit;

namespace PrepaidEngine.Tests.Conversion;

public class MockPaymentModeChangeClientTests
{
    private readonly MockPaymentModeChangeClient _client = new();

    private static PaymentModeChangeRequest NewRequest(string correlationId) => new(
        Guid.NewGuid(), "MTR-12345", correlationId,
        InitialReading: 1000m,
        InitialReadingDateTime: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        ConversionDate: new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task ChangePaymentModeAsync_DefaultCorrelationId_ReturnsAcknowledgedWithAnEstimatedReading()
    {
        var result = await _client.ChangePaymentModeAsync(NewRequest("TXN-0001"));

        Assert.Equal(PaymentModeChangeOutcome.Acknowledged, result.Outcome);
        Assert.NotNull(result.MeterReadingAtConversion);
        Assert.True(result.MeterReadingAtConversion > 1000m); // 10 days elapsed -> some consumption assumed
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData("PMCFAIL-abc")]
    [InlineData("prefix-PMCFAIL-suffix")]
    [InlineData("pmcfail-lowercase")]
    public async Task ChangePaymentModeAsync_CorrelationIdContainsPmcFail_ReturnsFailed(string correlationId)
    {
        var result = await _client.ChangePaymentModeAsync(NewRequest(correlationId));

        Assert.Equal(PaymentModeChangeOutcome.Failed, result.Outcome);
        Assert.Null(result.MeterReadingAtConversion);
        Assert.NotNull(result.Message);
    }

    [Theory]
    [InlineData("PMCTIMEOUT-abc")]
    [InlineData("prefix-PMCTIMEOUT-suffix")]
    public async Task ChangePaymentModeAsync_CorrelationIdContainsPmcTimeout_ReturnsTimedOut(string correlationId)
    {
        var result = await _client.ChangePaymentModeAsync(NewRequest(correlationId));

        Assert.Equal(PaymentModeChangeOutcome.TimedOut, result.Outcome);
        Assert.Null(result.MeterReadingAtConversion);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task ChangePaymentModeAsync_NullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _client.ChangePaymentModeAsync(null!));
    }
}
