using PrepaidEngine.Domain.Entities;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class AuditEntryTests
{
    private static AuditEntry NewEntry(
        string entityType = "ConnectivityCommand",
        string entityId = "abc-123",
        string action = "Dispatched",
        string actor = "system") =>
        new(Guid.NewGuid(), entityType, entityId, action, actor, DateTime.UtcNow);

    [Fact]
    public void Constructor_RecordsAllFields()
    {
        var occurredAt = DateTime.UtcNow;
        var entry = new AuditEntry(Guid.NewGuid(), "Tariff", "t-1", "VersionRecorded", "operator@example.com", occurredAt, "5.00", "5.25", "Rate change");

        Assert.Equal("Tariff", entry.EntityType);
        Assert.Equal("t-1", entry.EntityId);
        Assert.Equal("VersionRecorded", entry.Action);
        Assert.Equal("operator@example.com", entry.Actor);
        Assert.Equal(occurredAt, entry.OccurredAt);
        Assert.Equal("5.00", entry.OldValue);
        Assert.Equal("5.25", entry.NewValue);
        Assert.Equal("Rate change", entry.Details);
    }

    [Fact]
    public void Constructor_OldNewValueAndDetailsAreOptional()
    {
        var entry = NewEntry();

        Assert.Null(entry.OldValue);
        Assert.Null(entry.NewValue);
        Assert.Null(entry.Details);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingEntityType_Throws(string? entityType)
    {
        Assert.Throws<ArgumentException>(() => new AuditEntry(Guid.NewGuid(), entityType!, "1", "Action", "actor", DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingEntityId_Throws(string? entityId)
    {
        Assert.Throws<ArgumentException>(() => new AuditEntry(Guid.NewGuid(), "Type", entityId!, "Action", "actor", DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingAction_Throws(string? action)
    {
        Assert.Throws<ArgumentException>(() => new AuditEntry(Guid.NewGuid(), "Type", "1", action!, "actor", DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingActor_Throws(string? actor)
    {
        Assert.Throws<ArgumentException>(() => new AuditEntry(Guid.NewGuid(), "Type", "1", "Action", actor!, DateTime.UtcNow));
    }
}
