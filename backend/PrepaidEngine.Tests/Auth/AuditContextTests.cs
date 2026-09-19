using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Security;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.Auth;

public class AuditEntryContextTests
{
    private static AuditEntry NewEntry() => new(Guid.NewGuid(), "Thing", "1", "DONE", "alice", DateTime.UtcNow);

    [Fact]
    public void Attaches_role_address_and_correlation_id()
    {
        var e = NewEntry();
        e.AttachContext("IT", "10.0.0.7", "abc12345");
        Assert.Equal("IT", e.ActorRole);
        Assert.Equal("10.0.0.7", e.SourceIp);
        Assert.Equal("abc12345", e.CorrelationId);
    }

    [Fact]
    public void Never_overwrites_a_value_that_was_already_set()
    {
        var e = NewEntry();
        e.AttachContext("Admin", null, null);
        e.AttachContext("Operator", "10.0.0.7", "abc12345");
        Assert.Equal("Admin", e.ActorRole);
        Assert.Equal("10.0.0.7", e.SourceIp);
    }

    [Fact]
    public void Blank_values_stay_null_and_long_values_are_cut_to_the_column_size()
    {
        var e = NewEntry();
        e.AttachContext("  ", null, new string('x', 200));
        Assert.Null(e.ActorRole);
        Assert.Equal(64, e.CorrelationId!.Length);
    }
}

public class CorrelationTests
{
    [Theory]
    [InlineData("3f2b8c1e-4d5a-4b6c-8d7e-9f0a1b2c3d4e", true)]
    [InlineData("abcdef12", true)]
    [InlineData("short", false)]
    [InlineData("has space in it 123", false)]
    [InlineData("line\nbreak-12345", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_short_plain_ids_are_accepted(string? value, bool expected)
        => Assert.Equal(expected, Correlation.IsAcceptable(value));

    [Fact]
    public void A_bad_or_missing_id_is_replaced_by_a_fresh_one()
    {
        var a = Correlation.Resolve("bad id!");
        var b = Correlation.Resolve(null);
        Assert.True(Correlation.IsAcceptable(a));
        Assert.NotEqual(a, b);
        Assert.Equal("abcdef12", Correlation.Resolve("abcdef12"));
    }
}

/// <summary>The interceptor stamps audit entries with the current request's context when the DbContext saves.</summary>
public class AuditContextInterceptorTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly HttpContextAccessor _accessor = new();
    private readonly PrepaidEngineDbContext _db;

    public AuditContextInterceptorTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>()
            .UseSqlite(_connection).AddInterceptors(new AuditContextInterceptor(_accessor)).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Entries_saved_during_a_request_carry_role_address_and_correlation_id()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Operator") }, "test"));
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.20");
        http.Items[Correlation.ItemKey] = "req-abcdef123456";
        _accessor.HttpContext = http;

        _db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "Thing", "1", "DONE", "alice", DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var saved = await _db.AuditEntries.AsNoTracking().SingleAsync();
        Assert.Equal("Operator", saved.ActorRole);
        Assert.Equal("192.168.1.20", saved.SourceIp);
        Assert.Equal("req-abcdef123456", saved.CorrelationId);
    }

    [Fact]
    public async Task Entries_saved_outside_a_request_have_no_context()
    {
        _accessor.HttpContext = null;

        _db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "Thing", "1", "DONE", "system", DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var saved = await _db.AuditEntries.AsNoTracking().SingleAsync();
        Assert.Null(saved.ActorRole);
        Assert.Null(saved.SourceIp);
        Assert.Null(saved.CorrelationId);
    }

    [Fact]
    public async Task A_role_set_explicitly_is_kept()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity()); // not signed in yet, as during login
        _accessor.HttpContext = http;
        var entry = new AuditEntry(Guid.NewGuid(), "Auth", "bob", "LOGIN_SUCCEEDED", "bob", DateTime.UtcNow);
        entry.AttachContext("Utility", null, null);

        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync();

        Assert.Equal("Utility", (await _db.AuditEntries.AsNoTracking().SingleAsync()).ActorRole);
    }
}
