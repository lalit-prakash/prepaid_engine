using System.Security.Cryptography;
using System.Text;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Api.Auth;

/// <summary>A signed-in user: the login id, the name to show, and the role.</summary>
public sealed record AuthUser(string Username, string DisplayName, UserRole Role);

/// <summary>A configured user as the password reset needs to see it: who they are and where to send a code.</summary>
public sealed record UserAccount(string Username, string DisplayName, UserRole Role, string? Email);

/// <summary>
/// Validates credentials against the users configured in DemoAuth:Users. Each entry has a Username (the login id), an
/// optional DisplayName and Email (where a password reset code is sent), a Role, and a PasswordHash from
/// <see cref="PasswordHasher"/> (a plain Password is accepted for throwaway local setups only). The single
/// DemoAuth:Username/Password pair still works and maps to one IT user.
///
/// Login ids are matched ignoring case and surrounding spaces. A password chosen through the reset flow is stored in the
/// database and passed in as <c>overrideHash</c>; it replaces the configured password for that user.
/// </summary>
public sealed class UserStore
{
    private sealed record Entry(string Username, string DisplayName, string? Email, string? PasswordHash, string? Password, UserRole Role);

    // Verified against when the login id is unknown, so a missing user costs the same as a wrong password.
    private static readonly string DummyHash = PasswordHasher.Hash("not-a-real-password");

    private readonly List<Entry> _users = new();

    public UserStore(IConfiguration configuration)
    {
        foreach (var section in configuration.GetSection("DemoAuth:Users").GetChildren())
        {
            var username = section["Username"];
            var hash = section["PasswordHash"];
            var password = section["Password"];
            if (string.IsNullOrEmpty(username) || (string.IsNullOrEmpty(hash) && string.IsNullOrEmpty(password)))
                continue;
            var role = Enum.TryParse<UserRole>(section["Role"], ignoreCase: true, out var parsed) ? parsed : UserRole.IT;
            var display = string.IsNullOrWhiteSpace(section["DisplayName"]) ? username : section["DisplayName"]!;
            var email = string.IsNullOrWhiteSpace(section["Email"]) ? null : section["Email"]!.Trim();
            _users.Add(new Entry(username, display, email, hash, password, role));
        }

        if (_users.Count == 0)
        {
            var username = configuration["DemoAuth:Username"];
            var password = configuration["DemoAuth:Password"];
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                _users.Add(new Entry(username, username, null, null, password, UserRole.IT));
        }
    }

    private Entry? Lookup(string? loginId)
        => string.IsNullOrWhiteSpace(loginId) ? null : _users.FirstOrDefault(u => string.Equals(u.Username, loginId.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The configured user for a login id (case and surrounding spaces ignored), or null.</summary>
    public UserAccount? Find(string? loginId)
        => Lookup(loginId) is { } e ? new UserAccount(e.Username, e.DisplayName, e.Role, e.Email) : null;

    public AuthUser? Validate(string username, string password, string? overrideHash = null)
    {
        var entry = Lookup(username);
        if (entry is null)
        {
            PasswordHasher.Verify(password, DummyHash);
            return null;
        }

        var hash = overrideHash ?? entry.PasswordHash;
        var ok = hash is not null
            ? PasswordHasher.Verify(password, hash)
            : FixedTimeEquals(password, entry.Password!);
        return ok ? new AuthUser(entry.Username, entry.DisplayName, entry.Role) : null;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }
}
