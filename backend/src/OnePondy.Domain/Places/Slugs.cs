using System.Globalization;
using System.Text;

namespace OnePondy.Domain.Places;

public static class Slugs
{
    public const int MaxLength = 200;

    /// <summary>"Sri Aurobindo Ashram" → "sri-aurobindo-ashram". Accents are dropped; other scripts become "place".</summary>
    public static string FromName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxLength - 10)
        {
            slug = slug[..(MaxLength - 10)].TrimEnd('-');
        }

        return slug.Length == 0 ? "place" : slug;
    }

    /// <summary>Adds "-2", "-3"… when the slug is already taken.</summary>
    public static string WithSuffix(string slug, int number) => number <= 1 ? slug : $"{slug}-{number}";
}
