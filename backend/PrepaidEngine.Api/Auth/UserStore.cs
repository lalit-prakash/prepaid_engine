using System.Security.Cryptography;
using System.Text;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Api.Auth;

/// <summary>A signed-in user: the login id, the name to show, and the role.</summary>
public sealed record AuthUser(string Username, string DisplayName, UserRole Role);

/// <summary>
/// Validates credentials against the users configured in DemoAuth:Users. Each entry has a Username (the
/// login id), an optional DisplayName, a Role, and a PasswordHash from <see cref="PasswordHasher"/>
/// (a plain Password is accepted for throwaway local setups only). The single
/// DemoAuth:Username/Password pair still works and maps to one IT user. A real user table replaces
/// this later; what the rest of the API sees (<see cref="AuthUser"/>) stays the same.
/// </summary>
public sealed class UserStore
{
    private sealed record Entry(string Username, string DisplayName, string? PasswordHash, string? Password, UserRole Role);

    // Verified against when the username is unknown, so a missing user costs the same as a wrong password.
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
            _users.Add(new Entry(username, display, hash, password, role));
        }

        if (_users.Count == 0)
        {
            var username = configuration["DemoAuth:Username"];
            var password = configuration["DemoAuth:Password"];
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                _users.Add(new Entry(username, username, null, password, UserRole.IT));
        }
    }

    public AuthUser? Validate(string username, string password)
    {
        var entry = _users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.Ordinal));
        if (entry is null)
        {
            PasswordHasher.Verify(password, DummyHash);
            return null;
        }

        var ok = entry.PasswordHash is not null
            ? PasswordHasher.Verify(password, entry.PasswordHash)
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
