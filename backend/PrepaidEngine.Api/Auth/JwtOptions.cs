namespace PrepaidEngine.Api.Auth;

/// <summary>Bound from the "Jwt" configuration section. The signing key is a secret and must come from
/// user-secrets or an environment variable (Jwt__Key), never from a committed file.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "PrepaidEngine";
    public string Audience { get; set; } = "PrepaidEngine.Web";

    /// <summary>HMAC-SHA256 signing key, at least 32 characters.</summary>
    public string? Key { get; set; }

    /// <summary>Lifetime of one access token.</summary>
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>A session can be renewed until this long after the original sign-in, then the user must sign in again.</summary>
    public int MaxSessionHours { get; set; } = 8;
}
