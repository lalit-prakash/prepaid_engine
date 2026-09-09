using PrepaidEngine.Domain.Entities;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class TouPeriodTests
{
    [Theory]
    [InlineData("06:00", true)]
    [InlineData("12:00", true)]
    [InlineData("16:59:59", true)]
    [InlineData("17:00", false)]
    [InlineData("05:59:59", false)]
    public void Contains_NonWrappingPeriod(string time, bool expected)
    {
        var period = new TouPeriod("Normal", TimeSpan.FromHours(6), TimeSpan.FromHours(17), 5.55m);

        Assert.Equal(expected, period.Contains(TimeSpan.Parse(time)));
    }

    [Theory]
    [InlineData("23:00", true)]
    [InlineData("00:00", true)]
    [InlineData("03:00", true)]
    [InlineData("05:59:59", true)]
    [InlineData("06:00", false)]
    [InlineData("22:59:59", false)]
    public void Contains_WrappingPeriod(string time, bool expected)
    {
        var period = new TouPeriod("Off-Peak", TimeSpan.FromHours(23), TimeSpan.FromHours(6), 4.72m);

        Assert.Equal(expected, period.Contains(TimeSpan.Parse(time)));
    }

    [Fact]
    public void Constructor_StartEqualsEnd_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TouPeriod("Bad", TimeSpan.FromHours(6), TimeSpan.FromHours(6), 5.00m));
    }

    [Fact]
    public void Constructor_NegativeRate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TouPeriod("Bad", TimeSpan.FromHours(6), TimeSpan.FromHours(17), -1m));
    }

    [Fact]
    public void Constructor_EmptyLabel_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TouPeriod("", TimeSpan.FromHours(6), TimeSpan.FromHours(17), 5.00m));
    }

    [Fact]
    public void Constructor_TimeAtOrBeyond24Hours_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TouPeriod("Bad", TimeSpan.FromHours(6), TimeSpan.FromHours(24), 5.00m));
    }
}
