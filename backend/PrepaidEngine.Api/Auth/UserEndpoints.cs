using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Auth;

public sealed record CreateUserRequest(string? LoginId, string? DisplayName, string? Email, string? Role, string? Password);
public sealed record UpdateUserRequest(string? DisplayName, string? Email, string? Role, bool? IsActive);
public sealed record SetPasswordRequest(string? Password);

/// <summary>
/// User Management: list, create, edit, activate / deactivate, set a password and unlock users, plus the roles and what each may do.
/// Everything needs the IT policy (Admin or IT) and is audited. Users defined in configuration are listed but read-only here. Guards:
/// nobody can deactivate or change the role of themselves, and at least one active Admin or IT user must always remain, so the system
/// cannot lock everyone out. A deactivated user can no longer sign in or renew a session; a token already issued runs out on its own
/// (at most the access-token lifetime).
/// </summary>
public static class UserEndpoints
{
    private static readonly Regex LoginIdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9_.\-]{2,49}$", RegexOptions.Compiled);
    private static readonly UserRole[] Managers = { UserRole.Admin, UserRole.IT };

    private static object Shape(DirectoryUser u, DateTime? createdAt, DateTime? lastLoginAt, DateTime? passwordChangedAt, bool locked) => new
    {
        u.Id,
        u.LoginId,
        u.DisplayName,
        Email = u.Email ?? "",
        Role = u.Role.ToString(),
        u.IsActive,
        Source = u.Source.ToString(),
        CreatedAt = createdAt,
        LastLoginAt = lastLoginAt,
        PasswordChangedAt = passwordChangedAt,
        Locked = locked,
    };

    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/users", async (string? q, string? role, string? status, PrepaidEngineDbContext db, UserDirectory directory, LoginThrottle throttle) =>
        {
            var now = DateTime.UtcNow;
            var stored = await db.Users.AsNoTracking().OrderBy(u => u.LoginKey).Take(1000).ToListAsync();
            var rows = stored.Select(u => (User: new DirectoryUser(u.LoginId, u.DisplayName, u.Role, u.Email, u.IsActive, UserSource.Database, u.Id), u.CreatedAt, (DateTime?)u.LastLoginAt, (DateTime?)u.PasswordChangedAt))
                .Concat(directory.ConfiguredUsers().Select(c => (User: c, CreatedAt: default(DateTime), (DateTime?)null, (DateTime?)null)))
                .ToList();

            IEnumerable<(DirectoryUser User, DateTime CreatedAt, DateTime? LastLoginAt, DateTime? PasswordChangedAt)> filtered = rows;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                filtered = filtered.Where(r => r.User.LoginId.Contains(term, StringComparison.OrdinalIgnoreCase) || r.User.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (r.User.Email ?? "").Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            if (Enum.TryParse<UserRole>(role, ignoreCase: true, out var wantedRole)) filtered = filtered.Where(r => r.User.Role == wantedRole);
            if (status == "active") filtered = filtered.Where(r => r.User.IsActive);
            else if (status == "inactive") filtered = filtered.Where(r => !r.User.IsActive);

            var items = filtered.OrderBy(r => r.User.LoginId, StringComparer.OrdinalIgnoreCase)
                .Select(r => Shape(r.User, r.User.Source == UserSource.Database ? r.CreatedAt : null, r.LastLoginAt, r.PasswordChangedAt, throttle.RetryAfter(r.User.LoginId, now) is not null))
                .ToList();
            return Results.Ok(new { items, total = items.Count });
        })
        .WithName("ListUsers")
        .RequireAuthorization(AccessPolicies.ITRole);

        app.MapPost("/api/v1/users", async (CreateUserRequest request, ClaimsPrincipal principal, PrepaidEngineDbContext db, UserDirectory directory) =>
        {
            var problems = new List<string>();
            var loginId = request.LoginId?.Trim() ?? "";
            if (!LoginIdPattern.IsMatch(loginId)) problems.Add("Login id must be 3 to 50 characters: letters, digits, dot, dash or underscore, starting with a letter or digit.");
            if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 150) problems.Add("Enter the person's name (up to 150 characters).");
            if (!ValidEmail(request.Email)) problems.Add("Enter a valid e-mail address; password reset codes are sent there.");
            if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role)) problems.Add("Choose a role.");
            problems.AddRange(PasswordPolicy.Problems(request.Password, loginId));
            if (problems.Count > 0) return Results.BadRequest(new { error = problems[0], problems });
            if (await directory.ExistsAsync(loginId)) return Results.Conflict(new { error = $"A user with login id '{loginId}' already exists." });

            var actor = principal.Identity?.Name ?? "unknown";
            var now = DateTime.UtcNow;
            var user = new AppUser(Guid.NewGuid(), loginId, request.DisplayName!, request.Email!, role, PasswordHasher.Hash(request.Password!), now, actor);
            db.Users.Add(user);
            db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "User", user.LoginId, "USER_CREATED", actor, now, newValue: $"{user.Role}, {user.Email}"));
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/users/{user.Id}", Shape(new DirectoryUser(user.LoginId, user.DisplayName, user.Role, user.Email, true, UserSource.Database, user.Id), user.CreatedAt, null, user.PasswordChangedAt, false));
        })
        .WithName("CreateUser")
        .RequireAuthorization(AccessPolicies.ITRole);

        app.MapPut("/api/v1/users/{id:guid}", async (Guid id, UpdateUserRequest request, ClaimsPrincipal principal, PrepaidEngineDbContext db, UserDirectory directory, LoginThrottle throttle) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound(new { error = "No such user." });

            var problems = new List<string>();
            var displayName = request.DisplayName ?? user.DisplayName;
            var email = request.Email ?? user.Email;
            var role = user.Role;
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 150) problems.Add("Enter the person's name (up to 150 characters).");
            if (!ValidEmail(email)) problems.Add("Enter a valid e-mail address.");
            if (request.Role is not null && (!Enum.TryParse(request.Role, ignoreCase: true, out role) || !Enum.IsDefined(role))) problems.Add("Choose a role.");
            if (problems.Count > 0) return Results.BadRequest(new { error = problems[0], problems });

            var active = request.IsActive ?? user.IsActive;
            var actor = principal.Identity?.Name ?? "unknown";
            var isSelf = string.Equals(actor, user.LoginId, StringComparison.OrdinalIgnoreCase);
            if (isSelf && (!active || role != user.Role)) return Results.BadRequest(new { error = "You cannot deactivate yourself or change your own role." });

            // At least one active Admin or IT user (in the database or configuration) must remain.
            var wasManager = user.IsActive && Managers.Contains(user.Role);
            var willBeManager = active && Managers.Contains(role);
            if (wasManager && !willBeManager)
            {
                var others = await db.Users.CountAsync(u => u.Id != id && u.IsActive && (u.Role == UserRole.Admin || u.Role == UserRole.IT))
                    + directory.ConfiguredUsers().Count(c => Managers.Contains(c.Role));
                if (others == 0) return Results.BadRequest(new { error = "At least one active Admin or IT user must remain." });
            }

            var wasActive = user.IsActive;
            var before = $"{user.DisplayName}, {user.Email}, {user.Role}, {(user.IsActive ? "active" : "inactive")}";
            var now = DateTime.UtcNow;
            user.UpdateProfile(displayName, email, role, now);
            user.SetActive(active, now);
            var after = $"{user.DisplayName}, {user.Email}, {user.Role}, {(user.IsActive ? "active" : "inactive")}";
            var action = wasActive != user.IsActive ? (user.IsActive ? "USER_ACTIVATED" : "USER_DEACTIVATED") : "USER_UPDATED";
            db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "User", user.LoginId, action, actor, now, before, after));
            await db.SaveChangesAsync();
            return Results.Ok(Shape(new DirectoryUser(user.LoginId, user.DisplayName, user.Role, user.Email, user.IsActive, UserSource.Database, user.Id), user.CreatedAt, user.LastLoginAt, user.PasswordChangedAt, throttle.RetryAfter(user.LoginId, now) is not null));
        })
        .WithName("UpdateUser")
        .RequireAuthorization(AccessPolicies.ITRole);

        app.MapPost("/api/v1/users/{id:guid}/password", async (Guid id, SetPasswordRequest request, ClaimsPrincipal principal, PrepaidEngineDbContext db, UserDirectory directory, LoginThrottle throttle) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound(new { error = "No such user." });
            var problems = PasswordPolicy.Problems(request.Password, user.LoginId);
            if (problems.Count > 0) return Results.BadRequest(new { error = problems[0], problems });

            var now = DateTime.UtcNow;
            await directory.SetPasswordAsync(new DirectoryUser(user.LoginId, user.DisplayName, user.Role, user.Email, user.IsActive, UserSource.Database, user.Id), request.Password!, now);
            throttle.RecordSuccess(user.LoginId);
            db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "User", user.LoginId, "USER_PASSWORD_SET", principal.Identity?.Name ?? "unknown", now));
            await db.SaveChangesAsync();
            return Results.Ok(new { message = "Password changed." });
        })
        .WithName("SetUserPassword")
        .RequireAuthorization(AccessPolicies.ITRole);

        app.MapPost("/api/v1/users/{loginId}/unlock", async (string loginId, ClaimsPrincipal principal, PrepaidEngineDbContext db, UserDirectory directory, LoginThrottle throttle) =>
        {
            var user = await directory.FindAsync(loginId);
            if (user is null) return Results.NotFound(new { error = "No such user." });
            throttle.RecordSuccess(user.LoginId);
            db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "User", user.LoginId, "USER_UNLOCKED", principal.Identity?.Name ?? "unknown", DateTime.UtcNow));
            await db.SaveChangesAsync();
            return Results.Ok(new { message = "The sign-in lock was cleared." });
        })
        .WithName("UnlockUser")
        .RequireAuthorization(AccessPolicies.ITRole);

        // The roles and what each may do: the same list the API's authorization policies are built from.
        app.MapGet("/api/v1/roles", async (PrepaidEngineDbContext db, UserDirectory directory) =>
        {
            var counts = (await db.Users.AsNoTracking().Where(u => u.IsActive).GroupBy(u => u.Role).Select(g => new { Role = g.Key, Count = g.Count() }).ToListAsync())
                .ToDictionary(x => x.Role, x => x.Count);
            foreach (var c in directory.ConfiguredUsers()) counts[c.Role] = counts.GetValueOrDefault(c.Role) + 1;

            return Results.Ok(new
            {
                Roles = AccessPolicies.Roles.Select(r => new { Role = r.Role.ToString(), r.Summary, ActiveUsers = counts.GetValueOrDefault(r.Role) }),
                Permissions = AccessPolicies.Permissions.Select(p => new { p.Policy, p.Name, p.Description, Roles = p.Roles.Select(r => r.ToString()) }),
            });
        })
        .WithName("ListRoles")
        .RequireAuthorization(AccessPolicies.ITRole);
    }

    private static bool ValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200) return false;
        try { return new MailAddress(email.Trim()).Address == email.Trim(); }
        catch (FormatException) { return false; }
    }
}
