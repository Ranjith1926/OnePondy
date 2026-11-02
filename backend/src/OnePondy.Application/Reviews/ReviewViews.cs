using OnePondy.Application.Common;
using OnePondy.Domain.Reviews;

namespace OnePondy.Application.Reviews;

/// <summary>A new review: exactly one of <paramref name="BookingId"/> and <paramref name="FoodOrderId"/> says what is being reviewed.</summary>
public sealed record ReviewInput(Guid? BookingId, Guid? FoodOrderId, int? Rating, string? Text);

public sealed record ReviewEditInput(uint Version, int? Rating, string? Text);

public sealed record ReplyInput(uint Version, string? Text);

/// <summary>A moderator's decision that needs a reason (reject, take down).</summary>
public sealed record ModerationInput(uint Version, string? Reason);

public sealed record ReviewReply(string Text, DateTimeOffset RepliedAt);

/// <summary>The average of a business's or listing's published reviews, shown only once there are enough of them to mean something.</summary>
public sealed record RatingSummary(double Average, int Count);

/// <summary>A published review as anyone sees it. The author is the first name they gave, never a phone number or an account.</summary>
public sealed record PublicReview(Guid Id, int Rating, string? Text, string AuthorName, DateTimeOffset PublishedAt, ReviewReply? Reply);

public sealed record PublicReviewPage(
    RatingSummary? Rating,
    IReadOnlyList<PublicReview> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>What a customer sees of their own review, including why one was not published.</summary>
public sealed record MyReview(
    Guid Id,
    Guid? BookingId,
    Guid? FoodOrderId,
    Guid BusinessId,
    string BusinessName,
    int Rating,
    string? Text,
    ReviewStatus Status,
    string? ModerationReason,
    ReviewReply? Reply,
    DateTimeOffset CreatedAt,
    bool CanEdit,
    uint Version);

/// <summary>A published review as the business sees it, with what it was about so the owner can answer it.</summary>
public sealed record PartnerReview(
    Guid Id,
    string Kind,
    Guid? ListingId,
    string? ListingTitle,
    int Rating,
    string? Text,
    string AuthorName,
    DateTimeOffset PublishedAt,
    ReviewReply? Reply,
    uint Version);

public sealed record AdminReviewQuery(ReviewStatus? Status, int? Page, int? PageSize);

/// <summary>A review in the moderators' queue, with what the automatic check found. The author's account and contact details are not part of it.</summary>
public sealed record AdminReview(
    Guid Id,
    string Kind,
    Guid BusinessId,
    string BusinessName,
    Guid? ListingId,
    string? ListingTitle,
    int Rating,
    string? Text,
    string AuthorName,
    ReviewStatus Status,
    IReadOnlyList<string> Flags,
    string? ModerationReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    ReviewReply? Reply,
    uint Version);

public static class ReviewErrors
{
    public static readonly AppError NotFound = new("review_not_found", ErrorKind.NotFound, "That review does not exist.");

    public static readonly AppError TransactionNotFound = new(
        "review_transaction_not_found",
        ErrorKind.NotFound,
        "That booking or order does not exist.");

    public static readonly AppError TargetNotFound = new(
        "review_target_not_found",
        ErrorKind.NotFound,
        "That listing or restaurant does not exist.");

    public static readonly AppError NotEligible = new(
        "review_not_eligible",
        ErrorKind.Conflict,
        "A booking can be reviewed once it is completed, and an order once it is delivered.");

    public static readonly AppError WindowClosed = new(
        "review_window_closed",
        ErrorKind.Conflict,
        "The time to review this has passed.");

    public static readonly AppError AlreadyReviewed = new(
        "review_already_exists",
        ErrorKind.Conflict,
        "You have already reviewed this.");

    public static readonly AppError NotEditable = new(
        "review_not_editable",
        ErrorKind.Conflict,
        "This review can no longer be changed.");

    public static readonly AppError AlreadyEdited = new(
        "review_already_edited",
        ErrorKind.Conflict,
        "A review can be changed only once.");

    public static readonly AppError EditWindowClosed = new(
        "review_edit_window_closed",
        ErrorKind.Conflict,
        "The time to change this review has passed.");

    public static readonly AppError Changed = new(
        "review_changed",
        ErrorKind.Conflict,
        "This review changed while you were looking at it. Reload and try again.");

    public static readonly AppError WrongState = new(
        "review_wrong_state",
        ErrorKind.Conflict,
        "This review is not in a state where that can be done.");

    public static readonly AppError CannotReply = new(
        "review_cannot_reply",
        ErrorKind.Conflict,
        "Only a published review that has no reply yet can be answered.");

    public static AppError NeedsOneSubject() => AppError.Validation(
        new Dictionary<string, string[]> { ["bookingId"] = ["Say which booking or which order you are reviewing."] });

    public static AppError NeedsOneTarget() => AppError.Validation(
        new Dictionary<string, string[]> { ["listingId"] = ["Give either listingId or restaurantId."] });
}
