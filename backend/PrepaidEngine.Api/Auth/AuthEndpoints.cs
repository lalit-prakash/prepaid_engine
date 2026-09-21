using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Auth;

public sealed record LoginRequest(string? Username, string? Password);
public sealed record ForgotPasswordRequest(string? Username);
public sealed record ResetPasswordRequest(string? Username, string? Code, string? NewPassword);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/auth/login", async (LoginRequest request, HttpContext http, UserStore users, LoginThrottle throttle, TokenService tokens, PrepaidEngineDbContext db, ILogger<LoginThrottle> log) =>
        {
            var now = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
                return Results.BadRequest(new { error = "Login id and password are required." });

            if (throttle.RetryAfter(request.Username, now) is { } wait)
            {
                log.LogWarning("Sign-in blocked for {User}: too many failed attempts.", request.Username);
                await RecordAsync(db, log, request.Username, "LOGIN_BLOCKED", null, "Too many failed attempts.");
                var seconds = (int)Math.Ceiling(wait.TotalSeconds);
                http.Response.Headers.RetryAfter = seconds.ToString();
                return Results.Json(new { error = "Too many failed attempts. Try again later.", retryAfterSeconds = seconds }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            // A password chosen through "Forgot password" lives in the database and replaces the configured one.
            var known = users.Find(request.Username)?.Username;
            var overrideHash = known is null ? null : await db.UserPasswordOverrides.AsNoTracking().Where(o => o.LoginId == known).Select(o => o.PasswordHash).FirstOrDefaultAsync();
            var user = users.Validate(request.Username, request.Password, overrideHash);
            if (user is null)
            {
                throttle.RecordFailure(request.Username, now);
                log.LogWarning("Failed sign-in for {User}.", request.Username);
                await RecordAsync(db, log, request.Username, "LOGIN_FAILED", null, "Invalid login id or password.");
                return Results.Json(new { error = "Invalid login id or password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            throttle.RecordSuccess(request.Username);
            log.LogInformation("{User} signed in as {Role}.", user.Username, user.Role);
            await RecordAsync(db, log, user.Username, "LOGIN_SUCCEEDED", user.Role.ToString(), null);
            return Results.Ok(Response(tokens.Issue(user, now, now), user));
        }).AllowAnonymous().RequireRateLimiting(PrepaidEngine.Api.Security.SecurityExtensions.LoginLimiter).WithName("Login");

        // Forgot password, step 1: e-mail a one-time code to the address registered for the login id. The reply is the same
        // whether or not the login id exists, so this cannot be used to discover accounts.
        app.MapPost("/api/v1/auth/forgot-password", async (ForgotPasswordRequest request, HttpContext http, PasswordResetService reset, PrepaidEngine.Api.Auth.Email.IEmailSender email) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username))
                return Results.BadRequest(new { error = "Enter your login id." });
            if (!email.CanSend)
                return Results.Json(new { error = "E-mail is not set up on this server, so a reset code cannot be sent. Ask an administrator to reset your password." }, statusCode: StatusCodes.Status503ServiceUnavailable);

            await reset.RequestAsync(request.Username, http.Connection.RemoteIpAddress?.ToString(), DateTime.UtcNow, http.RequestAborted);
            return Results.Ok(new { message = "If that login id has a registered e-mail address, a 6-digit code has been sent to it. It is valid for 10 minutes." });
        }).AllowAnonymous().RequireRateLimiting(PrepaidEngine.Api.Security.SecurityExtensions.ResetLimiter).WithName("ForgotPassword");

        // Forgot password, step 2: the code plus a new password.
        app.MapPost("/api/v1/auth/reset-password", async (ResetPasswordRequest request, HttpContext http, PasswordResetService reset) =>
        {
            var result = await reset.ResetAsync(request.Username, request.Code, request.NewPassword, DateTime.UtcNow, http.RequestAborted);
            return result.Ok
                ? Results.Ok(new { message = "Your password has been changed. Sign in with the new password." })
                : Results.BadRequest(new { error = result.Error, problems = result.Problems });
        }).AllowAnonymous().RequireRateLimiting(PrepaidEngine.Api.Security.SecurityExtensions.ResetLimiter).WithName("ResetPassword");

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
        }).RequireAuthorization("Authenticated").WithName("RefreshToken");

        // Sign-out is client-side (a token cannot be revoked yet), so this only records the event.
        app.MapPost("/api/v1/auth/logout", async (ClaimsPrincipal principal, PrepaidEngineDbContext db, ILogger<LoginThrottle> log) =>
        {
            await RecordAsync(db, log, principal.Identity?.Name ?? "unknown", "LOGOUT", principal.FindFirstValue(ClaimTypes.Role), null);
            return Results.NoContent();
        }).RequireAuthorization("Authenticated").WithName("Logout");
    }

    private static object Response(IssuedToken token, AuthUser user) => new
    {
        accessToken = token.AccessToken,
        expiresAtUtc = token.ExpiresAtUtc,
        username = user.Username,
        displayName = user.DisplayName,
        role = user.Role.ToString(),
    };

    /// <summary>Writes one sign-in event to the audit trail. A failure to record never blocks or fails the sign-in itself.</summary>
    private static async Task RecordAsync(PrepaidEngineDbContext db, ILogger log, string username, string action, string? role, string? details)
    {
        try
        {
            var id = username.Length > 100 ? username[..100] : username;
            var entry = new AuditEntry(Guid.NewGuid(), "Auth", id, action, id, DateTime.UtcNow, details: details);
            entry.AttachContext(role, null, null); // the audit interceptor adds address and correlation id
            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not record {Action} for {User} in the audit trail.", action, username);
            db.ChangeTracker.Clear();
        }
    }
}
