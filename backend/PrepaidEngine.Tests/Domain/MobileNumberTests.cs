using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Tests.Domain;

public class MobileNumberTests
{
    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("91-9876543210", "+919876543210")]
    [InlineData("09876543210", "+919876543210")]
    [InlineData("(98765) 43210", "+919876543210")]
    [InlineData("  6000000001 ", "+916000000001")]
    public void Valid_numbers_are_stored_in_one_form(string input, string expected)
    {
        Assert.True(MobileNumber.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("5876543210")]        // must start 6-9
    [InlineData("98765432101")]       // too long
    [InlineData("98765 4321")]        // too short
    [InlineData("abcdefghij")]
    [InlineData("98765+43210")]       // a plus sign only means something at the start
    [InlineData("+1 9876543210")]     // another country code
    public void Invalid_numbers_are_rejected(string? input)
        => Assert.False(MobileNumber.TryNormalize(input, out _));

    [Fact]
    public void Mask_keeps_only_the_last_four_digits()
    {
        Assert.Equal("*********3210", MobileNumber.Mask("+919876543210"));
        Assert.Equal("—", MobileNumber.Mask(null));
    }

    [Fact]
    public void Consumer_stores_the_normalised_number_and_refuses_a_bad_one()
    {
        var consumer = new Consumer(Guid.NewGuid(), "A1", "Name", "Street", new SmartMeter(Guid.NewGuid(), "M1", MeterPhase.SinglePhase), 1m);

        consumer.SetMobileNumber("98765 43210");
        Assert.Equal("+919876543210", consumer.MobileNumber);

        Assert.Throws<ArgumentException>(() => consumer.SetMobileNumber("nope"));
        Assert.Equal("+919876543210", consumer.MobileNumber); // unchanged by the failed attempt
    }
}
