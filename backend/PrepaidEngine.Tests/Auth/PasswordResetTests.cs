using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Api.Auth.Email;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.Auth;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Test@123")]
    [InlineData("Str0ng#Passphrase")]
    public void Accepts_a_password_that_meets_every_rule(string password)
        => Assert.Empty(PasswordPolicy.Problems(password, "Admin_Lalit"));

    [Theory]
    [InlineData("")]
    [InlineData("Sh0rt!a")]
    [InlineData("alllowercase1!")]
    [InlineData("ALLUPPERCASE1!")]
    [InlineData("NoDigitsHere!")]
    [InlineData("NoSymbol1234")]
    public void Rejects_a_password_that_breaks_a_rule(string password)
        => Assert.NotEmpty(PasswordPolicy.Problems(password, "Admin_Lalit"));

    [Fact]
    public void Rejects_a_password_containing_the_login_id()
        => Assert.Contains(PasswordPolicy.Problems("xAdmin_Lalit1!", "admin_lalit"), p => p.Contains("login id"));
}

public class PasswordResetServiceTests : IDisposable
{
    private sealed class FakeEmail : IEmailSender
    {
        public bool CanSend => true;
        public readonly List<(string To, string Body)> Sent = new();
        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            lock (Sent) Sent.Add((to, body));
            return Task.CompletedTask;
        }
    }

    private static readonly DateTime T0 = new(2026, 9, 19, 10, 0, 0, DateTimeKind.Utc);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private readonly FakeEmail _email = new();
    private readonly UserStore _users;
    private readonly UserDirectory _directory;
    private readonly LoginThrottle _throttle = new();
    private readonly PasswordResetService _service;

    public PasswordResetServiceTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoAuth:Users:0:Username"] = "Admin_Lalit",
            ["DemoAuth:Users:0:DisplayName"] = "LALIT PRAKASH",
            ["DemoAuth:Users:0:Email"] = "lalit@example.com",
            ["DemoAuth:Users:0:Role"] = "Admin",
            ["DemoAuth:Users:0:Password"] = "Original@1",
            ["DemoAuth:Users:1:Username"] = "noemail",
            ["DemoAuth:Users:1:Password"] = "Original@1",
        }).Build();
        _users = new UserStore(config);
        _directory = new UserDirectory(_db, _users);
        _service = new PasswordResetService(_db, _directory, _email, _throttle, NullLogger<PasswordResetService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<string> RequestAndReadCodeAsync(string loginId, DateTime now)
    {
        var before = _email.Sent.Count;
        await _service.RequestAsync(loginId, "::1", now);
        for (var i = 0; i < 100 && _email.Sent.Count == before; i++) await Task.Delay(20);
        Assert.True(_email.Sent.Count > before, "no e-mail was sent");
        return Regex.Match(_email.Sent[^1].Body, @"\b\d{6}\b").Value;
    }

    [Fact]
    public async Task Sends_a_six_digit_code_to_the_registered_address()
    {
        var code = await RequestAndReadCodeAsync("Admin_Lalit", T0);
        Assert.Equal(6, code.Length);
        Assert.Equal("lalit@example.com", _email.Sent[0].To);
    }

    [Fact]
    public async Task Stores_only_a_hash_of_the_code()
    {
        var code = await RequestAndReadCodeAsync("Admin_Lalit", T0);
        var row = await _db.PasswordResetRequests.SingleAsync();
        Assert.DoesNotContain(code, row.OtpHash);
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("noemail")]
    [InlineData("")]
    public async Task Sends_nothing_for_an_unknown_login_id_or_one_without_an_email(string loginId)
    {
        await _service.RequestAsync(loginId, null, T0);
        await Task.Delay(100);
        Assert.Empty(_email.Sent);
        Assert.Empty(await _db.PasswordResetRequests.ToListAsync());
    }

    [Fact]
    public async Task Login_id_is_matched_ignoring_case_and_spaces()
    {
        await RequestAndReadCodeAsync("  admin_LALIT ", T0);
        Assert.Equal("Admin_Lalit", (await _db.PasswordResetRequests.SingleAsync()).LoginId);
    }

    [Fact]
    public async Task Correct_code_sets_the_new_password_and_clears_a_lockout()
    {
        for (var i = 0; i < LoginThrottle.MaxFailures; i++) _throttle.RecordFailure("Admin_Lalit", T0);
        Assert.NotNull(_throttle.RetryAfter("Admin_Lalit", T0));

        var code = await RequestAndReadCodeAsync("Admin_Lalit", T0);
        var result = await _service.ResetAsync("Admin_Lalit", code, "Test@123", T0.AddMinutes(2));

        Assert.True(result.Ok);
        Assert.Null(_throttle.RetryAfter("Admin_Lalit", T0.AddMinutes(2)));
        var hash = (await _db.UserPasswordOverrides.SingleAsync()).PasswordHash;
        Assert.NotNull(_users.Validate("Admin_Lalit", "Test@123", hash));
        Assert.Null(_users.Validate("Admin_Lalit", "Original@1", hash));
    }

    [Fact]
    public async Task A_code_works_only_once()
    {
        var code = await RequestAndReadCodeAsync("Admin_Lalit", T0);
        Assert.True((await _service.ResetAsync("Admin_Lalit", code, "Test@123", T0.AddMinutes(1))).Ok);
        Assert.False((await _service.ResetAsync("Admin_Lalit", code, "Other@1234", T0.AddMinutes(2))).Ok);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_five_wrong_codes_burn_the_request()
    {
        var code = await RequestAndReadCodeAsync("Admin_Lalit", T0);
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < PrepaidEngine.Domain.Entities.PasswordResetRequest.MaxAttempts; i++)
            Assert.False((await _service.ResetAsync("Admin_Lalit", wrong, "Test@123", T0.AddMinutes(1))).Ok);

        // even the right code no longer works
        Assert.False((await _service.ResetAsync("Admin_Lalit", code, "Test@123", T0.AddMinutes(1))).Ok);
        Assert.Empty(await _db.UserPasswordOverrides.ToListAsync());
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var code = await RequestAndReadCodeAsync("Admin_Lalit", T0);
        var result = await _service.ResetAsync("Admin_Lalit", code, "Test@123", T0 + PasswordResetService.CodeLifetime + TimeSpan.FromSeconds(1));
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task A_new_request_cancels_the_earlier_code()
    {
        var first = await RequestAndReadCodeAsync("Admin_Lalit", T0);
        var second = await RequestAndReadCodeAsync("Admin_Lalit", T0.AddMinutes(2));
        if (first != second)
            Assert.False((await _service.ResetAsync("Admin_Lalit", first, "Test@123", T0.AddMinutes(3))).Ok);
        Assert.True((await _service.ResetAsync("Admin_Lalit", second, "Test@123", T0.AddMinutes(3))).Ok);
    }

    [Fact]
    public async Task A_weak_password_is_refused_without_using_up_the_code()
    {
        var code = await RequestAndReadCodeAsync("Admin_Lalit", T0);
        var weak = await _service.ResetAsync("Admin_Lalit", code, "password", T0.AddMinutes(1));
        Assert.False(weak.Ok);
        Assert.NotEmpty(weak.Problems);
        Assert.True((await _service.ResetAsync("Admin_Lalit", code, "Test@123", T0.AddMinutes(1))).Ok);
    }

    [Fact]
    public async Task Codes_cannot_be_requested_more_than_once_a_minute()
    {
        await RequestAndReadCodeAsync("Admin_Lalit", T0);
        await _service.RequestAsync("Admin_Lalit", null, T0.AddSeconds(20));
        await Task.Delay(100);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task No_more_than_three_codes_an_hour()
    {
        for (var i = 0; i < PasswordResetService.MaxCodesPerHour; i++)
            await RequestAndReadCodeAsync("Admin_Lalit", T0.AddMinutes(i * 2));
        await _service.RequestAsync("Admin_Lalit", null, T0.AddMinutes(10));
        await Task.Delay(100);
        Assert.Equal(PasswordResetService.MaxCodesPerHour, _email.Sent.Count);
    }

    [Fact]
    public async Task Every_step_is_audited_and_no_code_is_written_to_the_audit_trail()
    {
        var code = await RequestAndReadCodeAsync("Admin_Lalit", T0);
        await _service.ResetAsync("Admin_Lalit", code == "000000" ? "111111" : "000000", "Test@123", T0.AddMinutes(1));
        await _service.ResetAsync("Admin_Lalit", code, "Test@123", T0.AddMinutes(1));

        var audit = await _db.AuditEntries.Where(a => a.EntityType == "Auth").ToListAsync();
        Assert.Contains(audit, a => a.Action == "PASSWORD_RESET_REQUESTED");
        Assert.Contains(audit, a => a.Action == "PASSWORD_RESET_FAILED");
        Assert.Contains(audit, a => a.Action == "PASSWORD_RESET_COMPLETED");
        Assert.DoesNotContain(audit, a => (a.Details ?? "").Contains(code));
    }
}
