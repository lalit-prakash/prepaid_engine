using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class ConversionRequestTests
{
    private static ConversionRequest NewRequest(
        DateTime? conversionDate = null,
        ConversionConsumerType consumerType = ConversionConsumerType.Residential,
        decimal outstandingAmount = 500m,
        decimal foaAmount = 100m,
        decimal diaAmount = 50m) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            transactionId: "TXN-001",
            meterSerialNumber: "MTR-12345",
            consumerNumber: "DEMO-0001",
            consumerType: consumerType,
            initialReading: 1200.5m,
            initialReadingDateTime: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            conversionDate: conversionDate ?? new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            requestedAt: DateTime.UtcNow,
            lastReadingDate: new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc),
            lastBillingDate: new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc),
            lastBillFrKwh: 1150m,
            lastBillFrKvah: 1200m,
            lastBillMaxDemandKw: 3m,
            outstandingAmount: outstandingAmount,
            meterStatus: ConversionMeterStatus.Normal,
            isPermanentConsumer: true,
            foaAmount: foaAmount,
            diaAmount: diaAmount);

    [Fact]
    public void Constructor_StartsInRequestedStatus()
    {
        var request = NewRequest();

        Assert.Equal(ConversionStatus.Requested, request.Status);
        Assert.Null(request.DecidedAt);
        Assert.Null(request.CompletedAt);
        Assert.Null(request.DecisionNote);
    }

    [Fact]
    public void Constructor_DefaultsRequestTypeToPre()
    {
        var request = NewRequest();

        Assert.Equal("PRE", request.RequestType);
    }

    [Theory]
    [InlineData(ConversionConsumerType.Vip)]
    [InlineData(ConversionConsumerType.Hospital)]
    [InlineData(ConversionConsumerType.School)]
    [InlineData(ConversionConsumerType.ShoppingComplex)]
    public void Constructor_RecordsTheConsumerType(ConversionConsumerType type)
    {
        var request = NewRequest(consumerType: type);

        Assert.Equal(type, request.ConsumerType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_MissingTransactionId_Throws(string? transactionId)
    {
        Assert.Throws<ArgumentException>(() => new ConversionRequest(
            Guid.NewGuid(), Guid.NewGuid(), transactionId!, "MTR-1", "DEMO-0001",
            ConversionConsumerType.Residential, 100m, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow,
            DateTime.UtcNow, DateTime.UtcNow, 100m, 100m, 3m, 0m, ConversionMeterStatus.Normal, true, 0m, 0m));
    }

    [Fact]
    public void Constructor_NegativeInitialReading_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionRequest(
            Guid.NewGuid(), Guid.NewGuid(), "TXN-1", "MTR-1", "DEMO-0001",
            ConversionConsumerType.Residential, -1m, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow,
            DateTime.UtcNow, DateTime.UtcNow, 100m, 100m, 3m, 0m, ConversionMeterStatus.Normal, true, 0m, 0m));
    }

    [Fact]
    public void Constructor_OutstandingAboveThresholdWithNonZeroFoaOrDia_Throws()
    {
        Assert.Throws<ArgumentException>(() => NewRequest(outstandingAmount: 10_001m, foaAmount: 1m, diaAmount: 0m));
        Assert.Throws<ArgumentException>(() => NewRequest(outstandingAmount: 10_001m, foaAmount: 0m, diaAmount: 1m));
    }

    [Fact]
    public void Constructor_OutstandingAboveThresholdWithZeroFoaAndDia_Succeeds()
    {
        var request = NewRequest(outstandingAmount: 15_000m, foaAmount: 0m, diaAmount: 0m);

        Assert.Equal(0m, request.FoaAmount);
        Assert.Equal(0m, request.DiaAmount);
    }

    [Fact]
    public void GracePeriodEndDate_SkipsWeekends_CountingFiveWorkingDays()
    {
        // Thursday 2026-09-10 + 5 working days = Fri, [Sat, Sun skipped], Mon, Tue, Wed, Thu -> 2026-09-17
        var request = NewRequest(conversionDate: new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 17), request.GracePeriodEndDate);
    }

    [Fact]
    public void IsWithinGracePeriod_OnEndDate_IsTrue()
    {
        var request = NewRequest(conversionDate: new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(request.IsWithinGracePeriod(request.GracePeriodEndDate));
        Assert.False(request.IsWithinGracePeriod(request.GracePeriodEndDate.AddDays(1)));
    }

    [Fact]
    public void Approve_FromRequested_TransitionsToApproved()
    {
        var request = NewRequest();
        var approvedAt = DateTime.UtcNow;

        request.Approve(approvedAt);

        Assert.Equal(ConversionStatus.Approved, request.Status);
        Assert.Equal(approvedAt, request.DecidedAt);
    }

    [Fact]
    public void Approve_WhenNotRequested_Throws()
    {
        var request = NewRequest();
        request.Approve(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => request.Approve(DateTime.UtcNow));
    }

    [Fact]
    public void Reject_FromRequested_TransitionsToRejected()
    {
        var request = NewRequest();

        request.Reject("NET meter consumers cannot be converted to prepaid.", DateTime.UtcNow);

        Assert.Equal(ConversionStatus.Rejected, request.Status);
        Assert.Equal("NET meter consumers cannot be converted to prepaid.", request.DecisionNote);
        Assert.NotNull(request.DecidedAt);
    }

    [Fact]
    public void Reject_WhenNotRequested_Throws()
    {
        var request = NewRequest();
        request.Approve(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => request.Reject("too late", DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Reject_MissingReason_Throws(string? reason)
    {
        var request = NewRequest();

        Assert.Throws<ArgumentException>(() => request.Reject(reason!, DateTime.UtcNow));
    }

    [Fact]
    public void RecordReadingAtConversion_ThenComplete_TransitionsToCompleted()
    {
        var request = NewRequest();
        request.Approve(DateTime.UtcNow);
        request.RecordReadingAtConversion(1250m);
        var completedAt = DateTime.UtcNow;

        request.Complete(completedAt);

        Assert.Equal(ConversionStatus.Completed, request.Status);
        Assert.Equal(completedAt, request.CompletedAt);
        Assert.Equal(1250m, request.ReadingAtConversion);
        Assert.Equal(1250m - 1200.5m, request.OpeningConsumptionKwh);
    }

    [Fact]
    public void Complete_WithoutRecordingReading_Throws()
    {
        var request = NewRequest();
        request.Approve(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => request.Complete(DateTime.UtcNow));
    }

    [Fact]
    public void Complete_WhenNotApproved_Throws()
    {
        var request = NewRequest();

        Assert.Throws<InvalidOperationException>(() => request.Complete(DateTime.UtcNow));
    }

    [Fact]
    public void Complete_AfterRejected_Throws()
    {
        var request = NewRequest();
        request.Reject("no", DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => request.Complete(DateTime.UtcNow));
    }

    [Fact]
    public void RecordReadingAtConversion_BelowInitialReading_Throws()
    {
        var request = NewRequest();
        request.Approve(DateTime.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() => request.RecordReadingAtConversion(1000m));
    }

    [Fact]
    public void RecordReadingAtConversion_CalledTwice_Throws()
    {
        var request = NewRequest();
        request.Approve(DateTime.UtcNow);
        request.RecordReadingAtConversion(1250m);

        Assert.Throws<InvalidOperationException>(() => request.RecordReadingAtConversion(1300m));
    }
}
