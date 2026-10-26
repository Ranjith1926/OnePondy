using OnePondy.Application.Common;

namespace OnePondy.Application.Places;

public static class PlaceErrors
{
    public static readonly AppError NotFound = new("place_not_found", ErrorKind.NotFound, "The place does not exist.");

    public static readonly AppError Changed = new(
        "place_changed",
        ErrorKind.Conflict,
        "Someone else changed this place. Reload to see their changes, then try again.");

    public static readonly AppError NotReadyForReview = new(
        "not_ready_for_review",
        ErrorKind.Validation,
        "Add a description and a location before submitting for review.");

    public static AppError InvalidTransition(string detail) => new("invalid_transition", ErrorKind.Conflict, detail);
}

public sealed class ContentOptions
{
    public const string SectionName = "Content";

    /// <summary>
    /// Verified places are flagged for a fresh check after this long. The spec leaves the interval to
    /// be agreed; 180 days is an assumption until then.
    /// </summary>
    public TimeSpan ReviewInterval { get; set; } = TimeSpan.FromDays(180);
}
