using System.Text.RegularExpressions;

namespace OnePondy.Domain.Identity;

/// <summary>Normalises phone numbers to E.164 and email addresses to lower case.</summary>
public static partial class ContactNormalizer
{
    public const string IndiaCallingCode = "91";

    /// <summary>
    /// Accepts E.164 (+919876543210), a 10-digit Indian mobile number (98765 43210), or the same
    /// number with a leading 0 or 91. Returns null when the input is not a valid number.
    /// </summary>
    public static string? NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        var digits = NonDigits().Replace(trimmed, string.Empty);

        if (trimmed.StartsWith('+'))
        {
            var e164 = "+" + digits;
            return E164().IsMatch(e164) ? e164 : null;
        }

        if (digits.Length == 11 && digits.StartsWith('0'))
        {
            digits = digits[1..];
        }
        else if (digits.Length == 12 && digits.StartsWith(IndiaCallingCode, StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        return IndianMobile().IsMatch(digits) ? $"+{IndiaCallingCode}{digits}" : null;
    }

    /// <summary>Returns the trimmed, lower-cased address, or null when it is not a plausible email.</summary>
    public static string? NormalizeEmail(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var email = input.Trim().ToLowerInvariant();
        return email.Length <= 254 && Email().IsMatch(email) ? email : null;
    }

    public static bool HasCallingCode(string e164Phone, string callingCode) =>
        e164Phone.StartsWith("+" + callingCode, StringComparison.Ordinal);

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex E164();

    // Indian mobile numbers are 10 digits starting with 6, 7, 8 or 9.
    [GeneratedRegex(@"^[6-9][0-9]{9}$")]
    private static partial Regex IndianMobile();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();
}
