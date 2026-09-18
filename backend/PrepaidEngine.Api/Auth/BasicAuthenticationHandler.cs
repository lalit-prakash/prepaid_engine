using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PrepaidEngine.Api.Auth;

public class BasicAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Minimal HTTP Basic authentication for the demo-only endpoints
/// (see docs/assumptions-and-security.md). Credentials come from configuration — never
/// hard-coded. This is a stop-gap for a local/demo deployment, not a substitute for real
/// authentication (token/OIDC + per-user authorization) before any shared or production
/// exposure.
///
/// Extended (not replaced) to carry a role claim for the tariff-governance workflow's two
/// business roles (<see cref="UserRole"/>): <c>DemoAuth:Users</c> is an array of
/// <c>{Username, Password, Role}</c>. The original single <c>DemoAuth:Username</c>/
/// <c>DemoAuth:Password</c> pair still works unchanged when <c>Users</c> is absent/empty — it is
/// treated as one IT-role user, so every endpoint that predates role-gating keeps working
/// exactly as before.
/// </summary>
public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;

    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header."));

        if (!AuthenticationHeaderValueTryParse(authorizationHeader.ToString(), out var username, out var password))
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization header."));

        var users = GetConfiguredUsers();
        if (users.Count == 0)
            return Task.FromResult(AuthenticateResult.Fail("Demo auth is not configured."));

        var matched = users.FirstOrDefault(u => FixedTimeEquals(username, u.Username) && FixedTimeEquals(password, u.Password));
        if (matched is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid username or password."));

        var claims = new[] { new Claim(ClaimTypes.Name, matched.Username), new Claim(ClaimTypes.Role, matched.Role.ToString()) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private sealed record DemoUser(string Username, string Password, PrepaidEngine.Domain.Enums.UserRole Role);

    private List<DemoUser> GetConfiguredUsers()
    {
        var users = new List<DemoUser>();

        var usersSection = _configuration.GetSection("DemoAuth:Users");
        foreach (var userSection in usersSection.GetChildren())
        {
            var username = userSection["Username"];
            var password = userSection["Password"];
            var roleRaw = userSection["Role"];
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                continue;

            var role = Enum.TryParse<PrepaidEngine.Domain.Enums.UserRole>(roleRaw, ignoreCase: true, out var parsedRole)
                ? parsedRole
                : PrepaidEngine.Domain.Enums.UserRole.IT;
            users.Add(new DemoUser(username, password, role));
        }

        if (users.Count > 0)
            return users;

        // Backward-compatible fallback: the original single-credential shape, always IT role.
        var expectedUsername = _configuration["DemoAuth:Username"];
        var expectedPassword = _configuration["DemoAuth:Password"];
        if (!string.IsNullOrEmpty(expectedUsername) && !string.IsNullOrEmpty(expectedPassword))
            users.Add(new DemoUser(expectedUsername, expectedPassword, PrepaidEngine.Domain.Enums.UserRole.IT));

        return users;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Basic realm=\"PrepaidEngine Demo\"";
        return base.HandleChallengeAsync(properties);
    }

    private static bool AuthenticationHeaderValueTryParse(string headerValue, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        const string prefix = "Basic ";
        if (!headerValue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var encoded = headerValue[prefix.Length..].Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var separatorIndex = decoded.IndexOf(':');
            if (separatorIndex < 0)
                return false;

            username = decoded[..separatorIndex];
            password = decoded[(separatorIndex + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);

        if (aBytes.Length != bBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
