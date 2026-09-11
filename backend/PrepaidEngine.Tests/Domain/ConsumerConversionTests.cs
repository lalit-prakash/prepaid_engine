using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class ConsumerConversionTests
{
    private static Consumer NewConsumer() =>
        new(Guid.NewGuid(), "ACC-1", "Test Consumer", "Address", new SmartMeter(Guid.NewGuid(), "MTR-1", MeterPhase.SinglePhase), 5m);

    [Fact]
    public void NewConsumer_DefaultsToPrepaid()
    {
        var consumer = NewConsumer();

        Assert.Equal(BillingMode.Prepaid, consumer.BillingMode);
    }

    [Fact]
    public void ConvertToPostpaid_FromPrepaid_Succeeds()
    {
        var consumer = NewConsumer();

        consumer.ConvertToPostpaid();

        Assert.Equal(BillingMode.Postpaid, consumer.BillingMode);
    }

    [Fact]
    public void ConvertToPostpaid_AlreadyPostpaid_Throws()
    {
        var consumer = NewConsumer();
        consumer.ConvertToPostpaid();

        Assert.Throws<InvalidOperationException>(() => consumer.ConvertToPostpaid());
    }

    [Fact]
    public void ConvertToPrepaid_FromPostpaid_Succeeds()
    {
        var consumer = NewConsumer();
        consumer.ConvertToPostpaid();

        consumer.ConvertToPrepaid();

        Assert.Equal(BillingMode.Prepaid, consumer.BillingMode);
    }

    [Fact]
    public void ConvertToPrepaid_AlreadyPrepaid_Throws()
    {
        var consumer = NewConsumer();

        Assert.Throws<InvalidOperationException>(() => consumer.ConvertToPrepaid());
    }
}
