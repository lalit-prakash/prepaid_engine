using System.Text.RegularExpressions;

namespace PrepaidEngine.Domain;

/// <summary>
/// Indian mobile numbers as stored for notifications: <c>+91</c> followed by ten digits starting 6 to 9. Input may
/// carry spaces, dashes or brackets and an optional <c>+91</c>, <c>91</c> or leading <c>0</c>; it is normalised so the
/// same number is always stored (and searched) in one form.
/// </summary>
public static class MobileNumber
{
    private static readonly Regex Ten = new(@"^[6-9]\d{9}$", RegexOptions.Compiled);

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (input.Contains('+') && !input.TrimStart().StartsWith("+")) return false; // a plus sign only means something at the start

        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        else if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];

        if (!Ten.IsMatch(digits)) return false;
        normalized = "+91" + digits;
        return true;
    }

    /// <summary>Shows only the last four digits, for places (like the audit trail) that should not hold the full number.</summary>
    public static string Mask(string? number)
        => string.IsNullOrEmpty(number) || number.Length < 4 ? "—" : new string('*', number.Length - 4) + number[^4..];
}
