using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth.Email;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Auth;

public sealed record ResetResult(bool Ok, string? Error, IReadOnlyList<string> Problems);

/// <summary>
/// Forgot-password by e-mail code. A person asks for a code by login id; if that login id belongs to a user with a
/// registered e-mail address, a 6-digit code goes there. The reply never says whether the login id exists, so the form cannot
/// be used to find out who has an account. A code is valid for 10 minutes, allows 5 wrong guesses and works once; a new request
/// cancels the previous code; at most 3 codes an hour and one a minute per login id. Only a salted hash of a code is stored.
/// Choosing the new password needs the code and a password that passes <see cref="PasswordPolicy"/>. Every step is audited.
/// </summary>
public class PasswordResetService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);
    public const int MaxCodesPerHour = 3;
    private const int Iterations = 100_000;

    private readonly PrepaidEngineDbContext _db;
    private readonly UserDirectory _users;
    private readonly IEmailSender _email;
    private readonly LoginThrottle _throttle;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(PrepaidEngineDbContext db, UserDirectory users, IEmailSender email, LoginThrottle throttle, ILogger<PasswordResetService> logger)
    {
        _db = db;
        _users = users;
        _email = email;
        _throttle = throttle;
        _logger = logger;
    }

    /// <summary>Issues and e-mails a code when the login id is known and has an e-mail address. Otherwise does nothing, and looks the same to the caller.</summary>
    public async Task RequestAsync(string? loginId, string? ip, DateTime nowUtc, CancellationToken ct = default)
    {
        var user = await _users.FindAsync(loginId, ct);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.Email))
        {
            if (user is not null && user.IsActive) _logger.LogWarning("Password reset requested for {User}, but no e-mail address is registered for that login id.", user.LoginId);
            return;
        }

        var recent = await _db.PasswordResetRequests.AsNoTracking()
            .Where(r => r.LoginId == user.LoginId && r.RequestedAt > nowUtc.AddHours(-1))
            .OrderByDescending(r => r.RequestedAt).Select(r => r.RequestedAt).ToListAsync(ct);
        if (recent.Count >= MaxCodesPerHour || (recent.Count > 0 && nowUtc - recent[0] < Cooldown))
        {
            _logger.LogWarning("Password reset code for {User} not sent: asked for too often.", user.LoginId);
            return;
        }

        foreach (var open in await _db.PasswordResetRequests.Where(r => r.LoginId == user.LoginId && r.UsedAt == null && r.SupersededAt == null).ToListAsync(ct))
            open.Supersede(nowUtc);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var salt = RandomNumberGenerator.GetBytes(16);
        _db.PasswordResetRequests.Add(new PasswordResetRequest(Guid.NewGuid(), user.LoginId, Hash(code, salt), Convert.ToBase64String(salt), nowUtc, CodeLifetime, ip));
        _db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "Auth", user.LoginId, "PASSWORD_RESET_REQUESTED", user.LoginId, nowUtc, details: "A reset code was e-mailed to the registered address."));
        await _db.SaveChangesAsync(ct);

        // Sent in the background so a slow mail server does not make "known" and "unknown" login ids look different.
        var to = user.Email!;
        var name = user.DisplayName;
        _ = Task.Run(async () =>
        {
            try
            {
                await _email.SendAsync(to, "Your Prepaid Engine password reset code",
                    $"Hello {name},\n\nYour password reset code is {code}.\nIt is valid for {(int)CodeLifetime.TotalMinutes} minutes and can be used once.\n\n" +
                    "If you did not ask to reset your password, ignore this message; your password stays unchanged.\n");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not e-mail the password reset code for {User}.", user.LoginId);
            }
        });
    }

    public async Task<ResetResult> ResetAsync(string? loginId, string? code, string? newPassword, DateTime nowUtc, CancellationToken ct = default)
    {
        const string Invalid = "That code is not valid or has expired. Request a new code.";
        var user = await _users.FindAsync(loginId, ct);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(code))
        {
            _ = Hash("000000", new byte[16]); // same work as a real check
            return new ResetResult(false, Invalid, Array.Empty<string>());
        }

        var request = (await _db.PasswordResetRequests.Where(r => r.LoginId == user.LoginId).OrderByDescending(r => r.RequestedAt).Take(5).ToListAsync(ct))
            .FirstOrDefault(r => r.IsUsable(nowUtc));
        if (request is null) return new ResetResult(false, Invalid, Array.Empty<string>());

        var expected = Convert.FromBase64String(request.OtpHash);
        var actual = Convert.FromBase64String(Hash(code.Trim(), Convert.FromBase64String(request.Salt)));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            request.RecordWrongGuess();
            _db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "Auth", user.LoginId, "PASSWORD_RESET_FAILED", user.LoginId, nowUtc, details: $"Wrong code (attempt {request.Attempts} of {PasswordResetRequest.MaxAttempts})."));
            await _db.SaveChangesAsync(ct);
            return new ResetResult(false, request.Attempts >= PasswordResetRequest.MaxAttempts ? "Too many wrong codes. Request a new code." : Invalid, Array.Empty<string>());
        }

        var problems = PasswordPolicy.Problems(newPassword, user.LoginId);
        if (problems.Count > 0)
            return new ResetResult(false, "That password is not strong enough.", problems); // the code is not used up, so they can try again

        request.MarkUsed(nowUtc);
        foreach (var other in await _db.PasswordResetRequests.Where(r => r.LoginId == user.LoginId && r.UsedAt == null && r.SupersededAt == null && r.Id != request.Id).ToListAsync(ct))
            other.Supersede(nowUtc);
        _db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "Auth", user.LoginId, "PASSWORD_RESET_COMPLETED", user.LoginId, nowUtc));
        // One save covers the used code, the audit entry and the new password, so a code is never spent without the password changing.
        await _users.SetPasswordAsync(user, newPassword!, nowUtc, ct);

        _throttle.RecordSuccess(user.LoginId); // a locked-out user who has just proved they own the account can sign in
        return new ResetResult(true, null, Array.Empty<string>());
    }

    private static string Hash(string code, byte[] salt)
        => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(code, salt, Iterations, HashAlgorithmName.SHA256, 32));
}
