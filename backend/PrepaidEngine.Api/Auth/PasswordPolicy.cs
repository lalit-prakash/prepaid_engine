namespace PrepaidEngine.Api.Auth;

/// <summary>Rules for a password chosen through the reset flow. Returns every problem at once so the person can fix them together.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static IReadOnlyList<string> Problems(string? password, string? loginId = null)
    {
        var problems = new List<string>();
        if (string.IsNullOrEmpty(password)) return new[] { "Enter a new password." };

        if (password.Length < MinLength) problems.Add($"Use at least {MinLength} characters.");
        if (password.Length > MaxLength) problems.Add($"Use at most {MaxLength} characters.");
        if (!password.Any(char.IsUpper)) problems.Add("Include an upper-case letter.");
        if (!password.Any(char.IsLower)) problems.Add("Include a lower-case letter.");
        if (!password.Any(char.IsDigit)) problems.Add("Include a digit.");
        if (!password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))) problems.Add("Include a symbol such as @ # $ %.");
        if (!string.IsNullOrWhiteSpace(loginId) && password.Contains(loginId.Trim(), StringComparison.OrdinalIgnoreCase))
            problems.Add("Do not include your login id.");
        return problems;
    }
}
