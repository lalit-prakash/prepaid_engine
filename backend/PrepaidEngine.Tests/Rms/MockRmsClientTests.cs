using System.Linq;
using System.Threading.Tasks;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Infrastructure.Rms;
using Xunit;

namespace PrepaidEngine.Tests.Rms;

public class MockRmsClientTests
{
    private static RmsRechargeRequest BuildRequest(string idempotencyKey, decimal amount = 100m) =>
        new(Guid.NewGuid(), amount, idempotencyKey, Guid.NewGuid().ToString());

    [Fact]
    public async Task InitiateRecharge_DefaultKey_ReturnsSuccess()
    {
        var client = new MockRmsClient();

        var result = await client.InitiateRechargeAsync(BuildRequest("NORMAL-1"));

        Assert.Equal(RmsRechargeStatus.Success, result.Status);
        Assert.False(result.WasReplayed);
        Assert.StartsWith("RMS-", result.RmsReferenceId);
    }

    [Fact]
    public async Task InitiateRecharge_FailPrefixedKey_ReturnsFailed()
    {
        var client = new MockRmsClient();

        var result = await client.InitiateRechargeAsync(BuildRequest("FAIL-1"));

        Assert.Equal(RmsRechargeStatus.Failed, result.Status);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task InitiateRecharge_PendingPrefixedKey_ReturnsPending()
    {
        var client = new MockRmsClient();

        var result = await client.InitiateRechargeAsync(BuildRequest("PENDING-1"));

        Assert.Equal(RmsRechargeStatus.Pending, result.Status);
    }

    [Fact]
    public async Task InitiateRecharge_UnavailablePrefixedKey_ThrowsRmsUnavailableException()
    {
        var client = new MockRmsClient();

        await Assert.ThrowsAsync<RmsUnavailableException>(
            () => client.InitiateRechargeAsync(BuildRequest("UNAVAILABLE-1")));
    }

    [Fact]
    public async Task InitiateRecharge_DuplicateIdempotencyKey_ReplaysOriginalResultWithoutReprocessing()
    {
        var client = new MockRmsClient();
        var request = BuildRequest("NORMAL-DUP");

        var first = await client.InitiateRechargeAsync(request);
        var second = await client.InitiateRechargeAsync(request);

        Assert.False(first.WasReplayed);
        Assert.True(second.WasReplayed);
        Assert.Equal(first.RmsReferenceId, second.RmsReferenceId);
        Assert.Equal(first.Status, second.Status);
    }

    [Fact]
    public async Task InitiateRecharge_ConcurrentCallsWithSameIdempotencyKey_ProduceExactlyOneRmsReference()
    {
        var client = new MockRmsClient();
        var request = BuildRequest("NORMAL-CONCURRENT");

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.InitiateRechargeAsync(request)));

        // The correctness guarantee under concurrency is a single RMS reference id per
        // idempotency key — i.e. the recharge is never double-processed. WasReplayed itself is
        // only informational and racing callers may each observe it as false; that's fine.
        var distinctReferenceIds = results.Select(r => r.RmsReferenceId).Distinct().ToList();
        Assert.Single(distinctReferenceIds);
        Assert.True(results.All(r => r.Status == RmsRechargeStatus.Success));
    }

    [Fact]
    public async Task InitiateRecharge_NonPositiveAmount_Throws()
    {
        var client = new MockRmsClient();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.InitiateRechargeAsync(BuildRequest("NORMAL-2", amount: 0m)));
    }

    [Fact]
    public async Task GetTransactionStatus_KnownReference_ReturnsStatus()
    {
        var client = new MockRmsClient();
        var recharge = await client.InitiateRechargeAsync(BuildRequest("NORMAL-3"));

        var status = await client.GetTransactionStatusAsync(recharge.RmsReferenceId);

        Assert.Equal(recharge.RmsReferenceId, status.RmsReferenceId);
        Assert.Equal(RmsRechargeStatus.Success, status.Status);
    }

    [Fact]
    public async Task GetTransactionStatus_UnknownReference_ThrowsNotFound()
    {
        var client = new MockRmsClient();

        await Assert.ThrowsAsync<RmsTransactionNotFoundException>(
            () => client.GetTransactionStatusAsync("RMS-does-not-exist"));
    }
}
