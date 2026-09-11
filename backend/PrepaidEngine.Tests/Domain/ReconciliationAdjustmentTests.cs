using PrepaidEngine.Domain.Entities;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class ReconciliationAdjustmentTests
{
    private static ReconciliationAdjustment NewAdjustment(decimal amount = 250m, string reference = "RMS shadow-bill gap for Sep 2026") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "DEMO-0001", amount, DateTime.UtcNow, reference, balanceAfter: 500m, appliedAt: DateTime.UtcNow);

    [Fact]
    public void Constructor_RecordsFields()
    {
        var adjustment = NewAdjustment(amount: 250m);

        Assert.Equal(250m, adjustment.Amount);
        Assert.Equal("Reconciliation", adjustment.PaymentMode);
        Assert.Equal("DEMO-0001", adjustment.ConsumerNumber);
    }

    [Fact]
    public void Constructor_AllowsNegativeAmount()
    {
        var adjustment = NewAdjustment(amount: -150m);

        Assert.Equal(-150m, adjustment.Amount);
    }

    [Fact]
    public void Constructor_ZeroAmount_Throws()
    {
        Assert.Throws<ArgumentException>(() => NewAdjustment(amount: 0m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingReference_Throws(string? reference)
    {
        Assert.Throws<ArgumentException>(() => NewAdjustment(reference: reference!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingConsumerNumber_Throws(string? consumerNumber)
    {
        Assert.Throws<ArgumentException>(() => new ReconciliationAdjustment(
            Guid.NewGuid(), Guid.NewGuid(), consumerNumber!, 100m, DateTime.UtcNow, "reason", 100m, DateTime.UtcNow));
    }
}
