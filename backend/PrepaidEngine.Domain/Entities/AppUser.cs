using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A person who can sign in to the engine, created and managed from User Management. (Users defined in configuration still work as
/// bootstrap accounts but are not stored here.) The login id is unique ignoring case. A user is deactivated rather than deleted, so audit
/// entries that name them stay meaningful. Only a PBKDF2 hash of the password is stored.
/// </summary>
public class AppUser
{
    public Guid Id { get; private set; }
    public string LoginId { get; private set; } = string.Empty;

    /// <summary>The login id, trimmed and lower-cased: what uniqueness and look-ups use.</summary>
    public string LoginKey { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; }
    public DateTime? PasswordChangedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }

    public AppUser(Guid id, string loginId, string displayName, string email, UserRole role, string passwordHash, DateTime createdAt, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(loginId)) throw new ArgumentException("A login id is required.", nameof(loginId));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        Id = id;
        LoginId = loginId.Trim();
        LoginKey = Key(loginId);
        DisplayName = displayName.Trim();
        Email = email.Trim();
        Role = role;
        PasswordHash = passwordHash;
        CreatedAt = UpdatedAt = createdAt;
        PasswordChangedAt = createdAt;
        CreatedBy = createdBy;
    }

    // EF Core / serialization
    private AppUser() { }

    public static string Key(string loginId) => loginId.Trim().ToLowerInvariant();

    public void UpdateProfile(string displayName, string email, UserRole role, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A name is required.", nameof(displayName));
        DisplayName = displayName.Trim();
        Email = email.Trim();
        Role = role;
        UpdatedAt = at;
    }

    public void SetActive(bool active, DateTime at)
    {
        IsActive = active;
        UpdatedAt = at;
    }

    public void SetPasswordHash(string passwordHash, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        PasswordHash = passwordHash;
        PasswordChangedAt = UpdatedAt = at;
    }

    public void RecordLogin(DateTime at) => LastLoginAt = at;
}
