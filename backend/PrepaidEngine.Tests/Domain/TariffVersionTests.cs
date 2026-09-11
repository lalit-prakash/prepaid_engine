using PrepaidEngine.Domain.Entities;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class TariffVersionTests
{
    private static TariffVersion NewVersion(string changeNote = "Annual tariff revision") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "FixedChargePerUnitPerMonth", "180.00", "195.00", changeNote, DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public void Constructor_RecordsAllFields()
    {
        var version = NewVersion();

        Assert.Equal("FixedChargePerUnitPerMonth", version.FieldName);
        Assert.Equal("180.00", version.OldValue);
        Assert.Equal("195.00", version.NewValue);
        Assert.Equal("Annual tariff revision", version.ChangeNote);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingFieldName_Throws(string? fieldName)
    {
        Assert.Throws<ArgumentException>(
            () => new TariffVersion(Guid.NewGuid(), Guid.NewGuid(), fieldName!, "old", "new", "note", DateTime.UtcNow, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingChangeNote_Throws(string? note)
    {
        Assert.Throws<ArgumentException>(
            () => new TariffVersion(Guid.NewGuid(), Guid.NewGuid(), "Field", "old", "new", note!, DateTime.UtcNow, DateTime.UtcNow));
    }
}
