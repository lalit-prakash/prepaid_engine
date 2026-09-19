using System.Security.Claims;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Api.Auth;

public sealed record LoginRequest(string? Username, string? Password);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/auth/login", (LoginRequest request, HttpContext http, UserStore users, LoginThrottle throttle, TokenService tokens, ILogger<LoginThrottle> log) =>
        {
            var now = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
                return Results.BadRequest(new { error = "Login id and password are required." });

            if (throttle.RetryAfter(request.Username, now) is { } wait)
            {
                log.LogWarning("Sign-in blocked for {User}: too many failed attempts.", request.Username);
                http.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
                return Results.Json(new { error = "Too many failed attempts. Try again later." }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            var user = users.Validate(request.Username, request.Password);
            if (user is null)
            {
                throttle.RecordFailure(request.Username, now);
                log.LogWarning("Failed sign-in for {User}.", request.Username);
                return Results.Json(new { error = "Invalid login id or password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            throttle.RecordSuccess(request.Username);
            log.LogInformation("{User} signed in as {Role}.", user.Username, user.Role);
            return Results.Ok(Response(tokens.Issue(user, now, now), user));
        }).AllowAnonymous().RequireRateLimiting(PrepaidEngine.Api.Security.SecurityExtensions.LoginLimiter).WithName("Login");

        // Sliding renewal: a still-valid token is swapped for a fresh one until the session reaches its absolute limit.
        app.MapPost("/api/v1/auth/refresh", (ClaimsPrincipal principal, TokenService tokens) =>
        {
            var now = DateTime.UtcNow;
            if (!tokens.CanRenew(principal, now, out var authTime)
                || principal.Identity?.Name is not { } name
                || !Enum.TryParse<UserRole>(principal.FindFirstValue(ClaimTypes.Role), out var role))
                return Results.Json(new { error = "Session expired. Sign in again." }, statusCode: StatusCodes.Status401Unauthorized);

            var user = new AuthUser(name, principal.FindFirstValue(TokenService.DisplayNameClaim) ?? name, role);
            return Results.Ok(Response(tokens.Issue(user, authTime, now), user));
        }).RequireAuthorization().WithName("RefreshToken");
    }

    private static object Response(IssuedToken token, AuthUser user) => new
    {
        accessToken = token.AccessToken,
        expiresAtUtc = token.ExpiresAtUtc,
        username = user.Username,
        displayName = user.DisplayName,
        role = user.Role.ToString(),
    };
}
