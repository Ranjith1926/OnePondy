using System.Text.RegularExpressions;

namespace OnePondy.Application.Reviews;

/// <summary>
/// The automatic check on a review's text before a moderator sees it (spec §7: "checked for abuse and personal data before they are
/// published"). It only raises flags for the moderator; it never publishes or rejects a review by itself, so a false alarm costs a moderator a
/// glance and a miss is still caught by the person reading the review.
/// </summary>
public static partial class ReviewChecks
{
    public const string PhoneNumber = "phone_number";

    public const string EmailAddress = "email";

    public const string Link = "link";

    public const string BlockedWord = "blocked_word";

    /// <summary>The codes found in <paramref name="text"/>, in a fixed order, as one comma-separated value; null when the text is clean.</summary>
    public static string? Check(string? text, IReadOnlyCollection<string> blockedWords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var flags = new List<string>();
        if (PhonePattern().IsMatch(text))
        {
            flags.Add(PhoneNumber);
        }

        if (EmailPattern().IsMatch(text))
        {
            flags.Add(EmailAddress);
        }

        // An address's domain ("example.com" in "a@example.com") is already reported as an email, not again as a link.
        if (LinkPattern().IsMatch(EmailPattern().Replace(text, " ")))
        {
            flags.Add(Link);
        }

        if (ContainsBlockedWord(text, blockedWords))
        {
            flags.Add(BlockedWord);
        }

        return flags.Count == 0 ? null : string.Join(',', flags);
    }

    public static IReadOnlyList<string> Split(string? flags) =>
        string.IsNullOrEmpty(flags) ? [] : flags.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static bool ContainsBlockedWord(string text, IReadOnlyCollection<string> blockedWords)
    {
        foreach (var word in blockedWords)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word.Trim())}(?![\p{{L}}\p{{N}}])";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                return true;
            }
        }

        return false;
    }

    // Ten to fourteen digits, however they are spaced or punctuated: "98765 43210", "+91-98765-43210", "(0413) 233 4455".
    [GeneratedRegex(@"(?<!\d)(?:\+?\d[\s().\-]{0,2}){10,14}(?!\d)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex EmailPattern();

    // A web address with a scheme or "www.", or a bare name ending in a common domain.
    [GeneratedRegex(
        @"(?:https?://|www\.)\S+|\b[a-z0-9\-]+\.(?:com|in|net|org|io|co|app|me|info)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex LinkPattern();
}
