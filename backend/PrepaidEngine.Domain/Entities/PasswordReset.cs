namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A one-time code sent to a user's registered e-mail address so they can choose a new password. Only a salted hash of the
/// code is stored, never the code. A request is valid for a short time, allows a few wrong guesses, and works once.
/// </summary>
public class PasswordResetRequest
{
    public const int MaxAttempts = 5;

    public Guid Id { get; private set; }
    public string LoginId { get; private set; } = string.Empty;
    public string OtpHash { get; private set; } = string.Empty;
    public string Salt { get; private set; } = string.Empty;
    public DateTime RequestedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? UsedAt { get; private set; }

    /// <summary>Set when a newer request replaces this one, so an older code stops working.</summary>
    public DateTime? SupersededAt { get; private set; }

    public string? RequestedFromIp { get; private set; }

    public PasswordResetRequest(Guid id, string loginId, string otpHash, string salt, DateTime requestedAt, TimeSpan validFor, string? requestedFromIp)
    {
        if (string.IsNullOrWhiteSpace(loginId)) throw new ArgumentException("A login id is required.", nameof(loginId));
        Id = id;
        LoginId = loginId;
        OtpHash = otpHash;
        Salt = salt;
        RequestedAt = requestedAt;
        ExpiresAt = requestedAt + validFor;
        RequestedFromIp = requestedFromIp;
    }

    // EF Core / serialization
    private PasswordResetRequest() { }

    public bool IsUsable(DateTime nowUtc) => UsedAt is null && SupersededAt is null && Attempts < MaxAttempts && nowUtc < ExpiresAt;

    public void RecordWrongGuess() => Attempts++;

    public void MarkUsed(DateTime nowUtc) => UsedAt = nowUtc;

    public void Supersede(DateTime nowUtc) => SupersededAt ??= nowUtc;
}

/// <summary>
/// A password chosen through the reset flow. Users are defined in configuration, which the API cannot write to, so a reset
/// is stored here and takes precedence over the configured password hash for that login id.
/// </summary>
public class UserPasswordOverride
{
    public string LoginId { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; }

    public UserPasswordOverride(string loginId, string passwordHash, DateTime updatedAt)
    {
        if (string.IsNullOrWhiteSpace(loginId)) throw new ArgumentException("A login id is required.", nameof(loginId));
        LoginId = loginId;
        PasswordHash = passwordHash;
        UpdatedAt = updatedAt;
    }

    // EF Core / serialization
    private UserPasswordOverride() { }

    public void Replace(string passwordHash, DateTime updatedAt)
    {
        PasswordHash = passwordHash;
        UpdatedAt = updatedAt;
    }
}
