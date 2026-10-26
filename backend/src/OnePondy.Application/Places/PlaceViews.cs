using System.Globalization;
using OnePondy.Application.Media;
using OnePondy.Domain.Places;

namespace OnePondy.Application.Places;

public sealed record UserRef(Guid Id, string Name);

public sealed record OpeningPeriodView(DayOfWeek Day, string Opens, string Closes);

public sealed record OpeningHoursView(bool AlwaysOpen, IReadOnlyList<OpeningPeriodView> Periods, string? Notes)
{
    public static OpeningHoursView? From(OpeningHours? hours) => hours is null
        ? null
        : new OpeningHoursView(
            hours.AlwaysOpen,
            [.. hours.Periods.Select(p => new OpeningPeriodView(p.Day, Format(p.Opens), Format(p.Closes)))],
            hours.Notes);

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

public sealed record EntryFeeView(VisitorCategory Category, decimal Amount, string Currency)
{
    public static IReadOnlyList<EntryFeeView> From(IEnumerable<PlaceEntryFee> fees) =>
        [.. fees.OrderBy(f => f.Category).Select(f => new EntryFeeView(f.Category, f.Amount, PlaceEntryFee.Currency))];
}

public sealed record PlaceSummary(
    Guid Id,
    string Name,
    string Slug,
    string CategoryCode,
    VerificationStatus Status,
    bool IsPublished,
    bool IsReviewDue,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastReviewedAt);

public sealed record PlaceStatusCounts(int Draft, int Pending, int Verified, int Rejected, int Published, int ReviewDue);

/// <summary>Everything the console shows and edits for one place.</summary>
public sealed record PlaceDetail(
    Guid Id,
    string Slug,
    uint Version,
    string Name,
    string CategoryCode,
    string? Description,
    string? VisitorGuidance,
    string? Address,
    double? Latitude,
    double? Longitude,
    OpeningHoursView? OpeningHours,
    IReadOnlyList<EntryFeeView> EntryFees,
    IReadOnlyList<PlaceTranslationContent> Translations,
    VerificationStatus Status,
    bool IsPublished,
    bool IsReadyForReview,
    bool IsReviewDue,
    UserRef? VerifiedBy,
    DateTimeOffset? VerifiedAt,
    string? VerificationSource,
    DateTimeOffset? LastReviewedAt,
    string? ReviewNote,
    DateTimeOffset CreatedAt,
    UserRef CreatedBy,
    DateTimeOffset UpdatedAt,
    UserRef UpdatedBy,
    IReadOnlyList<MediaView> Media);

public sealed record CategoryView(string Code, string Name);
