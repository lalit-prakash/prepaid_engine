namespace PrepaidEngine.Api.Security;

/// <summary>Bound from the "Security" configuration section.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Browser origins allowed to call the API. Development defaults to the Angular dev server; other environments must list theirs.</summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    /// <summary>Requests per minute allowed from one client IP across the API (health checks excluded).</summary>
    public int RequestsPerMinutePerIp { get; set; } = 600;

    /// <summary>Sign-in attempts per minute allowed from one client IP (a per-account lockout also applies).</summary>
    public int LoginAttemptsPerMinutePerIp { get; set; } = 10;

    /// <summary>Largest accepted request body.</summary>
    public long MaxRequestBodyBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Set true only behind a trusted reverse proxy, so X-Forwarded-For/Proto are honoured (client IP for limiting, https detection).</summary>
    public bool TrustForwardedHeaders { get; set; }
}
