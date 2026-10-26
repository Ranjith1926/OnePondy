namespace OnePondy.Domain.Places;

/// <summary>Review state of a place's content (spec §10, Phase 3). Only verified places can be published.</summary>
public enum VerificationStatus
{
    Draft = 1,
    Pending = 2,
    Verified = 3,
    Rejected = 4,
}

/// <summary>Who an entry fee applies to; matches <c>place_entry_fees.visitor_category</c>.</summary>
public enum VisitorCategory
{
    Standard = 1,
    IndianAdult = 2,
    ForeignAdult = 3,
    Child = 4,
    Student = 5,
    Senior = 6,
}

/// <summary>How a photo may be used (spec D11 stays open, so every common source is covered).</summary>
public enum MediaLicence
{
    /// <summary>Taken or commissioned by OnePondy.</summary>
    Owned = 1,
    PartnerSupplied = 2,
    CcBy = 3,
    CcBySa = 4,
    Cc0 = 5,
    LicensedStock = 6,
    Other = 7,
}

public static class MediaLicenceRules
{
    /// <summary>Every licence except our own photos and public-domain (CC0) ones needs visible credit.</summary>
    public static bool RequiresAttribution(this MediaLicence licence) =>
        licence is not (MediaLicence.Owned or MediaLicence.Cc0);
}

/// <summary>Languages a place can be translated into; English is the base content (spec D10).</summary>
public static class ContentLanguages
{
    public const string Tamil = "ta";
    public const string French = "fr";

    public static readonly IReadOnlyList<string> Translations = [Tamil, French];

    public static bool IsTranslation(string language) => Translations.Contains(language);
}

public sealed record GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

public sealed record EntryFee(VisitorCategory Category, decimal Amount);

public sealed record PlaceTranslationContent(
    string Language,
    string Name,
    string? Description,
    string? VisitorGuidance);

/// <summary>Everything an editor can change in one save. English text is the base content.</summary>
public sealed record PlaceContent(
    string Name,
    string CategoryCode,
    string? Description,
    string? VisitorGuidance,
    string? Address,
    GeoPoint? Location,
    OpeningHours? OpeningHours,
    IReadOnlyList<EntryFee> EntryFees,
    IReadOnlyList<PlaceTranslationContent> Translations)
{
    public bool HasSameValuesAs(PlaceContent other) =>
        Name == other.Name
        && CategoryCode == other.CategoryCode
        && Description == other.Description
        && VisitorGuidance == other.VisitorGuidance
        && Address == other.Address
        && Location == other.Location
        && Equals(OpeningHours, other.OpeningHours)
        && EntryFees.OrderBy(f => f.Category).SequenceEqual(other.EntryFees.OrderBy(f => f.Category))
        && Translations.OrderBy(t => t.Language, StringComparer.Ordinal)
            .SequenceEqual(other.Translations.OrderBy(t => t.Language, StringComparer.Ordinal));
}
