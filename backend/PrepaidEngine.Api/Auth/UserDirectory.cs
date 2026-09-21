using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Auth;

/// <summary>Where a user is defined: created in User Management (Database) or defined in configuration (bootstrap accounts, read-only here).</summary>
public enum UserSource { Database, Configuration }

/// <summary>A user as sign-in and password reset see them, whichever place defines them.</summary>
public sealed record DirectoryUser(string LoginId, string DisplayName, UserRole Role, string? Email, bool IsActive, UserSource Source, Guid? Id);

/// <summary>
/// One view over every user who can sign in: those created in User Management (database) and the bootstrap users in configuration.
/// A database user wins when both exist (creating one with a configured login id is refused, so this is only a safety net). Sign-in
/// checks the password and that the user is active; a password chosen through reset is stored on the database user, or for a
/// configuration user in <c>UserPasswordOverrides</c>, where it takes precedence over the configured hash.
/// </summary>
public sealed class UserDirectory
{
    private static readonly string DummyHash = PasswordHasher.Hash("not-a-real-password");

    private readonly PrepaidEngineDbContext _db;
    private readonly UserStore _config;

    public UserDirectory(PrepaidEngineDbContext db, UserStore config)
    {
        _db = db;
        _config = config;
    }

    public async Task<DirectoryUser?> FindAsync(string? loginId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(loginId)) return null;
        var key = AppUser.Key(loginId);
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.LoginKey == key, ct);
        if (u is not null) return new DirectoryUser(u.LoginId, u.DisplayName, u.Role, u.Email, u.IsActive, UserSource.Database, u.Id);
        var c = _config.Find(loginId);
        return c is null ? null : new DirectoryUser(c.Username, c.DisplayName, c.Role, c.Email, true, UserSource.Configuration, null);
    }

    /// <summary>The signed-in identity when the login id and password are right and the user is active; otherwise null (a wrong login id costs the same as a wrong password).</summary>
    public async Task<AuthUser?> ValidateAsync(string loginId, string password, CancellationToken ct = default)
    {
        var key = AppUser.Key(loginId);
        var u = await _db.Users.FirstOrDefaultAsync(x => x.LoginKey == key, ct);
        if (u is not null)
        {
            var ok = PasswordHasher.Verify(password, u.PasswordHash);
            if (!ok || !u.IsActive) return null;
            u.RecordLogin(DateTime.UtcNow);
            await _db.SaveChangesAsync(ct);
            return new AuthUser(u.LoginId, u.DisplayName, u.Role);
        }

        var known = _config.Find(loginId)?.Username;
        var overrideHash = known is null ? null : await _db.UserPasswordOverrides.AsNoTracking().Where(o => o.LoginId == known).Select(o => o.PasswordHash).FirstOrDefaultAsync(ct);
        return _config.Validate(loginId, password, overrideHash);
    }

    /// <summary>Sets a new password for the user (already checked against the password policy by the caller).</summary>
    public async Task SetPasswordAsync(DirectoryUser user, string newPassword, DateTime nowUtc, CancellationToken ct = default)
    {
        var hash = PasswordHasher.Hash(newPassword);
        if (user.Source == UserSource.Database)
        {
            var entity = await _db.Users.FirstAsync(x => x.Id == user.Id, ct);
            entity.SetPasswordHash(hash, nowUtc);
        }
        else
        {
            var existing = await _db.UserPasswordOverrides.FirstOrDefaultAsync(o => o.LoginId == user.LoginId, ct);
            if (existing is null) _db.UserPasswordOverrides.Add(new UserPasswordOverride(user.LoginId, hash, nowUtc));
            else existing.Replace(hash, nowUtc);
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>The configured (bootstrap) users, for the user list.</summary>
    public IReadOnlyList<DirectoryUser> ConfiguredUsers() => _config.All().Select(c => new DirectoryUser(c.Username, c.DisplayName, c.Role, c.Email, true, UserSource.Configuration, null)).ToList();

    /// <summary>Whether the login id is taken by a database or configured user.</summary>
    public async Task<bool> ExistsAsync(string loginId, CancellationToken ct = default)
        => await FindAsync(loginId, ct) is not null;
}
