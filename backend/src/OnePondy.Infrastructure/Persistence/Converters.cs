using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Listings;
using OnePondy.Domain.Places;

namespace OnePondy.Infrastructure.Persistence;

/// <summary>Stores enum values as snake_case text, e.g. <c>IndianAdult</c> as <c>indian_adult</c> (spec §17).</summary>
public static class SnakeCaseEnum<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToText =
        Enum.GetValues<TEnum>().ToDictionary(value => value, value => SnakeCase(value.ToString()));

    private static readonly Dictionary<string, TEnum> FromText =
        ToText.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public static ValueConverter<TEnum, string> Converter { get; } =
        new(value => ToDatabase(value), text => FromDatabase(text));

    /// <summary>The SQL list of allowed values, for check constraints.</summary>
    public static string SqlList => string.Join(", ", ToText.Values.Select(v => $"'{v}'"));

    public static string ToDatabase(TEnum value) => ToText[value];

    public static TEnum FromDatabase(string text) => FromText[text];

    private static string SnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}

/// <summary>Opening hours as jsonb: <c>{"alwaysOpen":false,"periods":[{"day":"monday","opens":"09:00","closes":"17:00"}],"notes":null}</c>.</summary>
public static class OpeningHoursJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // EF Core never passes null to a converter, so the nullable signatures only satisfy the property type.
    public static ValueConverter<OpeningHours?, string> Converter { get; } =
        new(hours => Serialize(hours!), json => Deserialize(json));

    public static ValueComparer<OpeningHours?> Comparer { get; } =
        new((left, right) => Equals(left, right), hours => hours == null ? 0 : hours.GetHashCode(), hours => hours);

    public static string Serialize(OpeningHours hours) => JsonSerializer.Serialize(
        new Stored(
            hours.AlwaysOpen,
            [.. hours.Periods.Select(p => new StoredPeriod(
                p.Day.ToString().ToLowerInvariant(),
                p.Opens.ToString("HH:mm", CultureInfo.InvariantCulture),
                p.Closes.ToString("HH:mm", CultureInfo.InvariantCulture)))],
            hours.Notes),
        Options);

    public static OpeningHours Deserialize(string json)
    {
        var stored = JsonSerializer.Deserialize<Stored>(json, Options)
            ?? throw new InvalidOperationException("Opening hours JSON is empty.");
        var (hours, problems) = OpeningHours.Create(
            stored.AlwaysOpen,
            stored.Periods.Select(p => new OpeningPeriod(
                Enum.Parse<DayOfWeek>(p.Day, ignoreCase: true),
                TimeOnly.ParseExact(p.Opens, "HH:mm", CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(p.Closes, "HH:mm", CultureInfo.InvariantCulture))),
            stored.Notes);
        return hours ?? throw new InvalidOperationException("Stored opening hours are invalid: " + string.Join(" ", problems));
    }

    private sealed record Stored(bool AlwaysOpen, IReadOnlyList<StoredPeriod> Periods, string? Notes);

    private sealed record StoredPeriod(string Day, string Opens, string Closes);
}

/// <summary>A listing's cancellation policy as jsonb, or a booking's snapshot of it at reservation time.</summary>
public static class CancellationPolicyJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // EF Core never passes null to a converter, so the nullable signature only satisfies the property type.
    public static ValueConverter<CancellationPolicy?, string> Converter { get; } =
        new(policy => JsonSerializer.Serialize(policy, Options), json => JsonSerializer.Deserialize<CancellationPolicy>(json, Options));

    public static ValueComparer<CancellationPolicy?> Comparer { get; } =
        new((left, right) => Equals(left, right), policy => policy == null ? 0 : policy.GetHashCode(), policy => policy);

    /// <summary>For a booking's snapshot, which always has a policy (never null).</summary>
    public static ValueConverter<CancellationPolicy, string> RequiredConverter { get; } =
        new(policy => JsonSerializer.Serialize(policy, Options), json => JsonSerializer.Deserialize<CancellationPolicy>(json, Options)!);

    public static ValueComparer<CancellationPolicy> RequiredComparer { get; } =
        new((left, right) => Equals(left, right), policy => policy.GetHashCode(), policy => policy);
}

/// <summary>A booking's price breakdown as the customer saw it at reservation time, stored as jsonb.</summary>
public static class PriceSnapshotJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ValueConverter<PriceSnapshot, string> Converter { get; } =
        new(
            snapshot => JsonSerializer.Serialize(snapshot, Options),
            json => JsonSerializer.Deserialize<PriceSnapshot>(json, Options)!);

    public static ValueComparer<PriceSnapshot> Comparer { get; } =
        new((left, right) => Equals(left, right), snapshot => snapshot.GetHashCode(), snapshot => snapshot);
}
