namespace OnePondy.Domain.Reviews;

/// <summary>
/// Where a review stands (spec §7, Reviews workflow). Nothing is shown to the public until a moderator publishes it; a published review
/// can later be taken down. The author can still change or delete a review until it is rejected or taken down.
/// </summary>
public enum ReviewStatus
{
    Pending = 1,
    Published = 2,
    Rejected = 3,
    Removed = 4,
}

/// <summary>
/// One customer's rating and comment on one completed transaction (spec §7, Phase 10): a booking that reached <c>completed</c> or a food
/// order that was <c>delivered</c>. There is exactly one review per transaction (a unique index in the database, not only a check here).
/// Partners may reply once to a published review but can never edit, hide or delete it; only a moderator decides what is published.
/// </summary>
public sealed class Review
{
    public const int MinRating = 1;

    public const int MaxRating = 5;

    public const int MaxTextLength = 1000;

    public const int MaxReplyLength = 1000;

    public const int MaxReasonLength = 500;

    public const int MaxAuthorNameLength = 60;

    public const int MaxFlagsLength = 200;

    private Review()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerUserId { get; private set; }

    /// <summary>The booking reviewed, or null when it is a food order. Exactly one of this and <see cref="FoodOrderId"/> is set.</summary>
    public Guid? BookingId { get; private set; }

    public Guid? FoodOrderId { get; private set; }

    public Guid BusinessId { get; private set; }

    /// <summary>The listing a booking was for; null for a food order, whose subject is the restaurant (the business).</summary>
    public Guid? ListingId { get; private set; }

    public int Rating { get; private set; }

    public string? Text { get; private set; }

    /// <summary>What the public and the partner see as the author: the first name the customer gave, never a phone number or account id.</summary>
    public string AuthorName { get; private set; } = string.Empty;

    public ReviewStatus Status { get; private set; }

    /// <summary>
    /// What the automatic check found in the text, as comma-separated codes for the moderator (for example "phone_number,link"). The check
    /// never publishes or rejects anything by itself.
    /// </summary>
    public string? AutoCheckFlags { get; private set; }

    /// <summary>Why a moderator rejected the review or took it down; shown to the author.</summary>
    public string? ModerationReason { get; private set; }

    public Guid? ModeratedByUserId { get; private set; }

    public DateTimeOffset? ModeratedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public string? ReplyText { get; private set; }

    public DateTimeOffset? RepliedAt { get; private set; }

    public Guid? RepliedByUserId { get; private set; }

    /// <summary>When the transaction completed; the review window counts from here.</summary>
    public DateTimeOffset TransactionCompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Set when the author has used their one edit.</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public bool HasReply => ReplyText is not null;

    public static Review ForBooking(
        Guid customerUserId,
        Guid bookingId,
        Guid businessId,
        Guid listingId,
        int rating,
        string? text,
        string authorName,
        string? autoCheckFlags,
        DateTimeOffset transactionCompletedAt,
        DateTimeOffset now) =>
        Create(customerUserId, bookingId, null, businessId, listingId, rating, text, authorName, autoCheckFlags, transactionCompletedAt, now);

    public static Review ForFoodOrder(
        Guid customerUserId,
        Guid foodOrderId,
        Guid businessId,
        int rating,
        string? text,
        string authorName,
        string? autoCheckFlags,
        DateTimeOffset transactionCompletedAt,
        DateTimeOffset now) =>
        Create(customerUserId, null, foodOrderId, businessId, null, rating, text, authorName, autoCheckFlags, transactionCompletedAt, now);

    /// <summary>A moderator approves a review that is waiting.</summary>
    public void Publish(Guid moderatorUserId, DateTimeOffset now)
    {
        RequireStatus(ReviewStatus.Pending);
        Status = ReviewStatus.Published;
        ModeratedByUserId = moderatorUserId;
        ModeratedAt = now;
        PublishedAt = now;
        ModerationReason = null;
        UpdatedAt = now;
    }

    /// <summary>A moderator declines a review that is waiting. The reason is shown to the author.</summary>
    public void Reject(Guid moderatorUserId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RequireStatus(ReviewStatus.Pending);
        Status = ReviewStatus.Rejected;
        ModeratedByUserId = moderatorUserId;
        ModeratedAt = now;
        ModerationReason = reason;
        UpdatedAt = now;
    }

    /// <summary>A moderator takes down a published review (for example after a report). It leaves the average at once.</summary>
    public void Remove(Guid moderatorUserId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RequireStatus(ReviewStatus.Published);
        Status = ReviewStatus.Removed;
        ModeratedByUserId = moderatorUserId;
        ModeratedAt = now;
        ModerationReason = reason;
        UpdatedAt = now;
    }

    /// <summary>
    /// The author changes their rating or text, once. The review goes back to waiting for a moderator (a published one is hidden until it is
    /// approved again) and any reply to the old words is cleared, because it answered something that no longer exists.
    /// </summary>
    public void Edit(int rating, string? text, string? autoCheckFlags, DateTimeOffset now)
    {
        if (Status is not (ReviewStatus.Pending or ReviewStatus.Published))
        {
            throw new InvalidOperationException($"A {Status} review cannot be edited.");
        }

        if (EditedAt is not null)
        {
            throw new InvalidOperationException("A review can be edited only once.");
        }

        ValidateRating(rating);
        ValidateText(text);
        Rating = rating;
        Text = text;
        AutoCheckFlags = autoCheckFlags;
        Status = ReviewStatus.Pending;
        ModeratedByUserId = null;
        ModeratedAt = null;
        PublishedAt = null;
        ModerationReason = null;
        ReplyText = null;
        RepliedAt = null;
        RepliedByUserId = null;
        EditedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The business answers a published review, once. It cannot be changed afterwards.</summary>
    public void Reply(Guid partnerUserId, string text, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaxReplyLength)
        {
            throw new ArgumentException($"A reply is at most {MaxReplyLength} characters.", nameof(text));
        }

        RequireStatus(ReviewStatus.Published);
        if (HasReply)
        {
            throw new InvalidOperationException("This review already has a reply.");
        }

        ReplyText = text;
        RepliedAt = now;
        RepliedByUserId = partnerUserId;
        UpdatedAt = now;
    }

    private static Review Create(
        Guid customerUserId,
        Guid? bookingId,
        Guid? foodOrderId,
        Guid businessId,
        Guid? listingId,
        int rating,
        string? text,
        string authorName,
        string? autoCheckFlags,
        DateTimeOffset transactionCompletedAt,
        DateTimeOffset now)
    {
        ValidateRating(rating);
        ValidateText(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorName);
        if (authorName.Length > MaxAuthorNameLength)
        {
            throw new ArgumentException($"An author name is at most {MaxAuthorNameLength} characters.", nameof(authorName));
        }

        return new Review
        {
            Id = Guid.CreateVersion7(),
            CustomerUserId = customerUserId,
            BookingId = bookingId,
            FoodOrderId = foodOrderId,
            BusinessId = businessId,
            ListingId = listingId,
            Rating = rating,
            Text = text,
            AuthorName = authorName,
            Status = ReviewStatus.Pending,
            AutoCheckFlags = autoCheckFlags,
            TransactionCompletedAt = transactionCompletedAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static void ValidateRating(int rating)
    {
        if (rating is < MinRating or > MaxRating)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), $"A rating is from {MinRating} to {MaxRating}.");
        }
    }

    private static void ValidateText(string? text)
    {
        if (text is not null && (text.Length == 0 || text.Length > MaxTextLength))
        {
            throw new ArgumentException($"A review's text is between 1 and {MaxTextLength} characters, or left out.", nameof(text));
        }
    }

    private void RequireStatus(ReviewStatus required)
    {
        if (Status != required)
        {
            throw new InvalidOperationException($"A review that is {Status} cannot do that; it must be {required}.");
        }
    }
}
