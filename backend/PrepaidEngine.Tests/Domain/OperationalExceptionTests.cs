using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class OperationalExceptionTests
{
    private static OperationalException NewException(
        OperationalExceptionSourceType sourceType = OperationalExceptionSourceType.MeterCommand,
        string description = "Meter command failed to acknowledge credit.") =>
        new(Guid.NewGuid(), sourceType, Guid.NewGuid(), Guid.NewGuid(), description, DateTime.UtcNow);

    [Fact]
    public void Constructor_StartsOpen()
    {
        var exception = NewException();

        Assert.Equal(OperationalExceptionStatus.Open, exception.Status);
        Assert.Null(exception.ResolvedAt);
        Assert.Null(exception.ResolutionNote);
    }

    [Theory]
    [InlineData(OperationalExceptionSourceType.MeterCommand)]
    [InlineData(OperationalExceptionSourceType.ConnectivityCommand)]
    public void Constructor_RecordsSourceType(OperationalExceptionSourceType sourceType)
    {
        var exception = NewException(sourceType);

        Assert.Equal(sourceType, exception.SourceType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingDescription_Throws(string? description)
    {
        Assert.Throws<ArgumentException>(
            () => new OperationalException(Guid.NewGuid(), OperationalExceptionSourceType.MeterCommand, Guid.NewGuid(), Guid.NewGuid(), description!, DateTime.UtcNow));
    }

    [Fact]
    public void Resolve_FromOpen_TransitionsToResolved()
    {
        var exception = NewException();
        var resolvedAt = DateTime.UtcNow;

        exception.Resolve("Retried and confirmed credited manually.", resolvedAt);

        Assert.Equal(OperationalExceptionStatus.Resolved, exception.Status);
        Assert.Equal(resolvedAt, exception.ResolvedAt);
        Assert.Equal("Retried and confirmed credited manually.", exception.ResolutionNote);
    }

    [Fact]
    public void Resolve_AlreadyResolved_Throws()
    {
        var exception = NewException();
        exception.Resolve("first", DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() => exception.Resolve("second", DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_MissingNote_Throws(string? note)
    {
        var exception = NewException();

        Assert.Throws<ArgumentException>(() => exception.Resolve(note!, DateTime.UtcNow));
    }
}
