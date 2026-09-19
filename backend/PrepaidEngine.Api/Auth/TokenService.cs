using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PrepaidEngine.Api.Auth;

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>Issues short-lived HS256 access tokens. The auth_time claim records the original sign-in
/// so a session cannot be renewed forever.</summary>
public sealed class TokenService
{
    public const string AuthTimeClaim = "auth_time";
    public const string DisplayNameClaim = "display_name";
    private readonly JwtOptions _options;

    public TokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public static SymmetricSecurityKey SigningKey(JwtOptions options) => new(Encoding.UTF8.GetBytes(options.Key!));

    public IssuedToken Issue(AuthUser user, DateTime authTimeUtc, DateTime utcNow)
    {
        var expires = utcNow.AddMinutes(_options.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(DisplayNameClaim, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(AuthTimeClaim, new DateTimeOffset(authTimeUtc, TimeSpan.Zero).ToUnixTimeSeconds().ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var token = new JwtSecurityToken(
            _options.Issuer, _options.Audience, claims, notBefore: utcNow, expires: expires,
            signingCredentials: new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256));
        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>True while the session may still be renewed (within MaxSessionHours of the original sign-in).</summary>
    public bool CanRenew(ClaimsPrincipal principal, DateTime utcNow, out DateTime authTimeUtc)
    {
        authTimeUtc = default;
        if (!long.TryParse(principal.FindFirstValue(AuthTimeClaim), out var seconds)) return false;
        authTimeUtc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        return utcNow - authTimeUtc < TimeSpan.FromHours(_options.MaxSessionHours);
    }
}
