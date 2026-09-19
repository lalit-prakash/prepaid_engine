using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace PrepaidEngine.Api.Security;

public static class SecurityExtensions
{
    public const string CorsPolicy = "ConfiguredOrigins";
    public const string LoginLimiter = "login";
    private const string DevOrigin = "http://localhost:4200";

    public static SecurityOptions AddApiSecurity(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
        if (options.AllowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
            options.AllowedOrigins = new[] { DevOrigin };
        if (options.AllowedOrigins.Any(o => o.Trim() == "*"))
            throw new InvalidOperationException("Security:AllowedOrigins must list explicit origins; a wildcard is not allowed.");

        builder.Services.AddSingleton(options);

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = options.MaxRequestBodyBytes;
        });

        builder.Services.AddCors(c => c.AddPolicy(CorsPolicy, p =>
            p.WithOrigins(options.AllowedOrigins).WithMethods("GET", "POST", "PUT", "DELETE").WithExposedHeaders(ListCap.TruncatedHeader, Correlation.HeaderName).WithHeaders("Authorization", "Content-Type", Correlation.HeaderName)));

        builder.Services.AddRateLimiter(r =>
        {
            r.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            r.OnRejected = (ctx, _) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString();
                return ValueTask.CompletedTask;
            };

            r.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                ctx.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : RateLimitPartition.GetFixedWindowLimiter(Client(ctx), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.RequestsPerMinutePerIp,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            r.AddPolicy(LoginLimiter, ctx => RateLimitPartition.GetFixedWindowLimiter("login:" + Client(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.LoginAttemptsPerMinutePerIp,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
        });

        if (options.TrustForwardedHeaders)
        {
            builder.Services.Configure<ForwardedHeadersOptions>(f =>
            {
                f.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                f.ForwardLimit = 1;
            });
        }

        return options;
    }

    /// <summary>Order matters: forwarded headers first (real client IP), then headers/HSTS/HTTPS, CORS, and the limiter before authentication.</summary>
    public static void UseApiSecurity(this WebApplication app, SecurityOptions options)
    {
        if (options.TrustForwardedHeaders) app.UseForwardedHeaders();

        app.Use(async (ctx, next) =>
        {
            var correlationId = Correlation.Resolve(ctx.Request.Headers[Correlation.HeaderName].ToString());
            ctx.Items[Correlation.ItemKey] = correlationId;
            var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Request");
            using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                h[Correlation.HeaderName] = correlationId;
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "DENY";
                h["Referrer-Policy"] = "no-referrer";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                h["Cross-Origin-Resource-Policy"] = "same-site";
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    // JSON API: nothing to embed or script, and responses hold personal data so must not be cached.
                    h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                    h["Cache-Control"] = "no-store";
                }
                return Task.CompletedTask;
            });
            await next();
        });

        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.UseHttpsRedirection();
        app.UseCors(CorsPolicy);
        app.UseRateLimiter();
    }

    private static string Client(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
