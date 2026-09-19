using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Tests.Auth;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_verifies_only_the_right_password()
    {
        var hash = PasswordHasher.Hash("correct horse");
        Assert.True(PasswordHasher.Verify("correct horse", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }

    [Fact]
    public void Hashes_of_the_same_password_differ_because_of_the_salt()
        => Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2$x$y$z")]
    public void Malformed_stored_values_never_verify(string stored)
        => Assert.False(PasswordHasher.Verify("anything", stored));
}

public class LoginThrottleTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Locks_after_max_failures_and_reports_wait()
    {
        var t = new LoginThrottle();
        for (var i = 0; i < LoginThrottle.MaxFailures; i++) t.RecordFailure("Bob", T0);
        Assert.NotNull(t.RetryAfter("bob", T0.AddMinutes(1)));
    }

    [Fact]
    public void Below_the_limit_is_not_locked()
    {
        var t = new LoginThrottle();
        for (var i = 0; i < LoginThrottle.MaxFailures - 1; i++) t.RecordFailure("bob", T0);
        Assert.Null(t.RetryAfter("bob", T0));
    }

    [Fact]
    public void Lock_expires_and_success_resets()
    {
        var t = new LoginThrottle();
        for (var i = 0; i < LoginThrottle.MaxFailures; i++) t.RecordFailure("bob", T0);
        Assert.Null(t.RetryAfter("bob", T0 + LoginThrottle.LockDuration + TimeSpan.FromSeconds(1)));

        for (var i = 0; i < LoginThrottle.MaxFailures; i++) t.RecordFailure("amy", T0);
        t.RecordSuccess("amy");
        Assert.Null(t.RetryAfter("amy", T0));
    }
}

public class UserStoreTests
{
    private static UserStore Store(params (string Key, string Value)[] settings)
        => new(new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build());

    [Fact]
    public void Validates_a_hashed_user_and_returns_display_name_and_role()
    {
        var store = Store(
            ("DemoAuth:Users:0:Username", "Admin_Lalit"), ("DemoAuth:Users:0:DisplayName", "LALIT PRAKASH"),
            ("DemoAuth:Users:0:Role", "Utility"), ("DemoAuth:Users:0:PasswordHash", PasswordHasher.Hash("pw")));

        var user = store.Validate("Admin_Lalit", "pw");
        Assert.NotNull(user);
        Assert.Equal("LALIT PRAKASH", user!.DisplayName);
        Assert.Equal(UserRole.Utility, user.Role);
        Assert.Null(store.Validate("Admin_Lalit", "nope"));
        Assert.Null(store.Validate("someone-else", "pw"));
    }

    [Fact]
    public void No_configured_users_means_nobody_can_sign_in()
        => Assert.Null(Store().Validate("x", "y"));
}

public class TokenServiceTests
{
    private static readonly JwtOptions Options = new() { Key = new string('k', 40), AccessTokenMinutes = 30, MaxSessionHours = 8 };
    private static readonly AuthUser User = new("Admin_Lalit", "LALIT PRAKASH", UserRole.IT);
    private static readonly DateTime Now = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static ClaimsPrincipal Principal(string jwt)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        return new ClaimsPrincipal(new ClaimsIdentity(token.Claims));
    }

    [Fact]
    public void Issued_token_expires_after_the_configured_minutes_and_carries_the_claims()
    {
        var issued = new TokenService(Microsoft.Extensions.Options.Options.Create(Options)).Issue(User, Now, Now);
        Assert.Equal(Now.AddMinutes(30), issued.ExpiresAtUtc);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);
        Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Role && c.Value == "IT");
        Assert.Contains(token.Claims, c => c.Type == TokenService.DisplayNameClaim && c.Value == "LALIT PRAKASH");
    }

    [Fact]
    public void Session_can_be_renewed_until_the_absolute_limit()
    {
        var svc = new TokenService(Microsoft.Extensions.Options.Options.Create(Options));
        var principal = Principal(svc.Issue(User, Now, Now).AccessToken);

        Assert.True(svc.CanRenew(principal, Now.AddHours(7), out _));
        Assert.False(svc.CanRenew(principal, Now.AddHours(9), out _));
    }
}
