using System.Globalization;
using OnePondy.Application.Common;
using OnePondy.Domain.Places;

namespace OnePondy.Application.Places;

public sealed record PlaceInput(
    string? Name,
    string? CategoryCode,
    string? Description,
    string? VisitorGuidance,
    string? Address,
    double? Latitude,
    double? Longitude,
    OpeningHoursInput? OpeningHours,
    IReadOnlyList<EntryFeeInput>? EntryFees,
    IReadOnlyList<TranslationInput>? Translations);

/// <param name="Periods">Times are "HH:mm" in local time (IST).</param>
public sealed record OpeningHoursInput(bool AlwaysOpen, IReadOnlyList<OpeningPeriodInput>? Periods, string? Notes);

public sealed record OpeningPeriodInput(DayOfWeek Day, string? Opens, string? Closes);

public sealed record EntryFeeInput(VisitorCategory Category, decimal Amount);

public sealed record TranslationInput(string? Language, string? Name, string? Description, string? VisitorGuidance);

/// <summary>Turns console form input into validated place content, with errors keyed by field.</summary>
public static class PlaceInputValidator
{
    public const int MaxNameLength = 180;
    public const int MaxDescriptionLength = 5000;
    public const int MaxGuidanceLength = 2000;
    public const int MaxAddressLength = 500;
    public const decimal MaxFee = 1_000_000m;

    public static Result<PlaceContent> Validate(PlaceInput input, IReadOnlySet<string> activeCategories)
    {
        var errors = new FieldErrors();

        var name = errors.Required("name", input.Name, MaxNameLength);
        var category = input.CategoryCode?.Trim() ?? string.Empty;
        if (!activeCategories.Contains(category))
        {
            errors.Add("categoryCode", "Choose a category.");
        }

        var description = errors.Optional("description", input.Description, MaxDescriptionLength);
        var guidance = errors.Optional("visitorGuidance", input.VisitorGuidance, MaxGuidanceLength);
        var address = errors.Optional("address", input.Address, MaxAddressLength);
        var location = ValidateLocation(input.Latitude, input.Longitude, errors);
        var hours = ValidateOpeningHours(input.OpeningHours, errors);
        var fees = ValidateFees(input.EntryFees ?? [], errors);
        var translations = ValidateTranslations(input.Translations ?? [], errors);

        if (errors.Any)
        {
            return errors.ToError();
        }

        return new PlaceContent(name, category, description, guidance, address, location, hours, fees, translations);
    }

    private static GeoPoint? ValidateLocation(double? latitude, double? longitude, FieldErrors errors)
    {
        if (latitude is null && longitude is null)
        {
            return null;
        }

        if (latitude is not { } lat || longitude is not { } lng)
        {
            errors.Add(latitude is null ? "latitude" : "longitude", "Enter both latitude and longitude, or neither.");
            return null;
        }

        var point = new GeoPoint(Math.Round(lat, 6), Math.Round(lng, 6));
        if (point.Latitude is < -90 or > 90)
        {
            errors.Add("latitude", "Latitude must be between -90 and 90.");
        }

        if (point.Longitude is < -180 or > 180)
        {
            errors.Add("longitude", "Longitude must be between -180 and 180.");
        }

        return point;
    }

    private static OpeningHours? ValidateOpeningHours(OpeningHoursInput? input, FieldErrors errors)
    {
        if (input is null)
        {
            return null;
        }

        var periods = new List<OpeningPeriod>();
        foreach (var period in input.Periods ?? [])
        {
            if (!TryParseTime(period.Opens, out var opens) || !TryParseTime(period.Closes, out var closes))
            {
                errors.Add("openingHours", $"{period.Day}: enter times as HH:mm, for example 09:30.");
                continue;
            }

            periods.Add(new OpeningPeriod(period.Day, opens, closes));
        }

        var (hours, problems) = OpeningHours.Create(input.AlwaysOpen, periods, input.Notes);
        foreach (var problem in problems)
        {
            errors.Add("openingHours", problem);
        }

        // No periods, not always open and no notes means the editor left opening hours blank.
        return hours is { AlwaysOpen: false, Periods.Count: 0, Notes: null } ? null : hours;
    }

    private static List<EntryFee> ValidateFees(IReadOnlyList<EntryFeeInput> input, FieldErrors errors)
    {
        if (input.Select(f => f.Category).Distinct().Count() != input.Count)
        {
            errors.Add("entryFees", "Each visitor category can have only one fee.");
        }

        foreach (var fee in input)
        {
            if (fee.Amount < 0 || fee.Amount > MaxFee || decimal.Round(fee.Amount, 2) != fee.Amount)
            {
                errors.Add("entryFees", $"{fee.Category}: enter an amount from 0 to {MaxFee:N0} with at most two decimals.");
            }
        }

        return [.. input.Select(f => new EntryFee(f.Category, f.Amount))];
    }

    private static List<PlaceTranslationContent> ValidateTranslations(IReadOnlyList<TranslationInput> input, FieldErrors errors)
    {
        var translations = new List<PlaceTranslationContent>();
        foreach (var translation in input)
        {
            var language = translation.Language?.Trim() ?? string.Empty;
            if (!ContentLanguages.IsTranslation(language))
            {
                errors.Add("translations", "Translations must be Tamil (ta) or French (fr).");
                continue;
            }

            if (translations.Any(t => t.Language == language))
            {
                errors.Add("translations", $"There is more than one {language} translation.");
                continue;
            }

            var prefix = $"translations.{language}";
            var description = errors.Optional($"{prefix}.description", translation.Description, MaxDescriptionLength);
            var guidance = errors.Optional($"{prefix}.visitorGuidance", translation.VisitorGuidance, MaxGuidanceLength);
            if (string.IsNullOrWhiteSpace(translation.Name) && description is null && guidance is null)
            {
                // An empty translation tab is not a translation.
                continue;
            }

            var name = errors.Required($"{prefix}.name", translation.Name, MaxNameLength);
            translations.Add(new PlaceTranslationContent(language, name, description, guidance));
        }

        return translations;
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
