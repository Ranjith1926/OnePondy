using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Food;
using OnePondy.Domain.Listings;
using OnePondy.Domain.Reviews;

namespace OnePondy.Application.Reviews;

/// <summary>
/// Reviews of completed bookings and delivered food orders (spec §7 and Phase 10, step 10.1). The server decides who may review what: the
/// transaction must be the caller's own, finished, inside the review window and not yet reviewed (the last is also a unique index in the
/// database). A review waits for a moderator before anyone sees it; partners may reply to a published review once but never change it.
/// </summary>
public sealed class ReviewService(
    IOnePondyDbContext db,
    AuditTrail audit,
    IUserAccountStore accounts,
    IOptions<ReviewOptions> options,
    TimeProvider time)
{
    private const string AnonymousAuthor = "A customer";

    private const string BookingKind = "booking";

    private const string FoodOrderKind = "food_order";

    // --- The customer ---

    public async Task<Result<MyReview>> CreateAsync(Guid customerUserId, ReviewInput input, CancellationToken cancellationToken)
    {
        if ((input.BookingId is null) == (input.FoodOrderId is null))
        {
            return ReviewErrors.NeedsOneSubject();
        }

        var errors = new FieldErrors();
        var rating = ValidRating(input.Rating, errors);
        var text = errors.Optional("text", input.Text, Review.MaxTextLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        var settings = options.Value;
        var now = time.GetUtcNow();
        var flags = ReviewChecks.Check(text, settings.BlockedWords);
        var author = await AuthorNameAsync(customerUserId, cancellationToken);

        Review review;
        if (input.BookingId is { } bookingId)
        {
            var booking = await db.Bookings.AsNoTracking().Include(b => b.History)
                .SingleOrDefaultAsync(b => b.Id == bookingId && b.CustomerUserId == customerUserId, cancellationToken);
            if (booking is null)
            {
                return ReviewErrors.TransactionNotFound;
            }

            if (booking.Status != BookingStatus.Completed)
            {
                return ReviewErrors.NotEligible;
            }

            var completedAt = booking.History.Where(h => h.NewStatus == BookingStatus.Completed)
                .Select(h => h.CreatedAt).DefaultIfEmpty(booking.UpdatedAt).Max();
            if (now - completedAt > TimeSpan.FromDays(settings.WindowDays))
            {
                return ReviewErrors.WindowClosed;
            }

            if (await db.Reviews.AnyAsync(r => r.BookingId == bookingId, cancellationToken))
            {
                return ReviewErrors.AlreadyReviewed;
            }

            review = Review.ForBooking(customerUserId, bookingId, booking.BusinessId, booking.ListingId, rating, text, author, flags, completedAt, now);
        }
        else
        {
            var orderId = input.FoodOrderId!.Value;
            var order = await db.FoodOrders.AsNoTracking().Include(o => o.History)
                .SingleOrDefaultAsync(o => o.Id == orderId && o.CustomerUserId == customerUserId, cancellationToken);
            if (order is null)
            {
                return ReviewErrors.TransactionNotFound;
            }

            if (order.Status != FoodOrderStatus.Delivered)
            {
                return ReviewErrors.NotEligible;
            }

            var deliveredAt = order.History.Where(h => h.NewStatus == FoodOrderStatus.Delivered)
                .Select(h => h.CreatedAt).DefaultIfEmpty(order.UpdatedAt).Max();
            if (now - deliveredAt > TimeSpan.FromDays(settings.WindowDays))
            {
                return ReviewErrors.WindowClosed;
            }

            if (await db.Reviews.AnyAsync(r => r.FoodOrderId == orderId, cancellationToken))
            {
                return ReviewErrors.AlreadyReviewed;
            }

            review = Review.ForFoodOrder(customerUserId, orderId, order.BusinessId, rating, text, author, flags, deliveredAt, now);
        }

        db.Reviews.Add(review);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // The unique index refused it: two requests for the same transaction arrived together, and the other one won.
            db.ClearTracked();
            return ReviewErrors.AlreadyReviewed;
        }

        return await ToMineAsync(review, cancellationToken);
    }

    public async Task<IReadOnlyList<MyReview>> ListMineAsync(Guid customerUserId, CancellationToken cancellationToken)
    {
        var reviews = await db.Reviews.AsNoTracking()
            .Where(r => r.CustomerUserId == customerUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
        var names = await BusinessNamesAsync(reviews.Select(r => r.BusinessId), cancellationToken);
        var settings = options.Value;
        var now = time.GetUtcNow();
        return [.. reviews.Select(r => ToMine(r, names.GetValueOrDefault(r.BusinessId, string.Empty), settings, now))];
    }

    /// <summary>One change to a review, once, within the edit window. It goes back to waiting for a moderator.</summary>
    public async Task<Result<MyReview>> EditAsync(Guid customerUserId, Guid id, ReviewEditInput input, CancellationToken cancellationToken)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == id && r.CustomerUserId == customerUserId, cancellationToken);
        if (review is null)
        {
            return ReviewErrors.NotFound;
        }

        var errors = new FieldErrors();
        var rating = ValidRating(input.Rating, errors);
        var text = errors.Optional("text", input.Text, Review.MaxTextLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        var settings = options.Value;
        var now = time.GetUtcNow();
        if (review.Status is not (ReviewStatus.Pending or ReviewStatus.Published))
        {
            return ReviewErrors.NotEditable;
        }

        if (review.EditedAt is not null)
        {
            return ReviewErrors.AlreadyEdited;
        }

        if (now > review.CreatedAt + settings.EditWindow)
        {
            return ReviewErrors.EditWindowClosed;
        }

        db.ExpectVersion(review, input.Version);
        review.Edit(rating, text, ReviewChecks.Check(text, settings.BlockedWords), now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            db.ClearTracked();
            return ReviewErrors.Changed;
        }

        return await ToMineAsync(review, cancellationToken);
    }

    /// <summary>
    /// The author withdraws a review that is waiting or published. A review a moderator rejected or took down stays on record, so
    /// deleting and posting again cannot be used to get round a moderator's decision.
    /// </summary>
    public async Task<AppError?> DeleteAsync(Guid customerUserId, Guid id, CancellationToken cancellationToken)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == id && r.CustomerUserId == customerUserId, cancellationToken);
        if (review is null)
        {
            return ReviewErrors.NotFound;
        }

        if (review.Status is not (ReviewStatus.Pending or ReviewStatus.Published))
        {
            return ReviewErrors.NotEditable;
        }

        db.Reviews.Remove(review);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // --- Everyone ---

    /// <summary>
    /// Published reviews of one bookable listing, or of one restaurant (from its food orders), newest first, with the rating once there are
    /// enough reviews to show one. A listing or restaurant that is not visible to customers answers not found.
    /// </summary>
    public async Task<Result<PublicReviewPage>> ListPublicAsync(
        Guid? listingId, Guid? restaurantId, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        if ((listingId is null) == (restaurantId is null))
        {
            return ReviewErrors.NeedsOneTarget();
        }

        IQueryable<Review> query;
        if (listingId is { } listing)
        {
            var visible = await (
                from l in db.Listings.AsNoTracking()
                join b in db.Businesses.AsNoTracking() on l.BusinessId equals b.Id
                where l.Id == listing && l.Status == ListingStatus.Published && b.IsActive
                select l.Id).AnyAsync(cancellationToken);
            if (!visible)
            {
                return ReviewErrors.TargetNotFound;
            }

            query = db.Reviews.AsNoTracking().Where(r => r.ListingId == listing && r.Status == ReviewStatus.Published);
        }
        else
        {
            var restaurant = restaurantId!.Value;
            var visible = await db.Businesses.AsNoTracking().AnyAsync(
                b => b.Id == restaurant
                    && b.BusinessType == BusinessType.Restaurant
                    && b.ApprovalStatus == BusinessApprovalStatus.Approved
                    && b.IsActive,
                cancellationToken);
            if (!visible)
            {
                return ReviewErrors.TargetNotFound;
            }

            query = db.Reviews.AsNoTracking().Where(r => r.BusinessId == restaurant && r.FoodOrderId != null && r.Status == ReviewStatus.Published);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(cancellationToken);
        var sum = total == 0 ? 0 : await query.SumAsync(r => r.Rating, cancellationToken);
        var items = await query
            .OrderByDescending(r => r.PublishedAt)
            .ThenByDescending(r => r.Id)
            .Skip((p - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return new PublicReviewPage(
            ReviewRatings.Summary(total, sum, options.Value.MinCountToShow),
            [.. items.Select(r => new PublicReview(r.Id, r.Rating, r.Text, r.AuthorName, r.PublishedAt!.Value, ToReply(r)))],
            p,
            size,
            total);
    }

    // --- The business ---

    /// <summary>The published reviews of a business, for its owner to read and answer.</summary>
    public async Task<Result<PagedResult<PartnerReview>>> ListForBusinessAsync(
        Guid businessId, Guid ownerUserId, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        if (!await OwnsBusinessAsync(businessId, ownerUserId, cancellationToken))
        {
            return ReviewErrors.NotFound;
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.Reviews.AsNoTracking().Where(r => r.BusinessId == businessId && r.Status == ReviewStatus.Published);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.PublishedAt)
            .ThenByDescending(r => r.Id)
            .Skip((p - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        var titles = await ListingTitlesAsync(items.Select(r => r.ListingId), cancellationToken);
        return new PagedResult<PartnerReview>([.. items.Select(r => ToPartner(r, titles))], p, size, total);
    }

    /// <summary>The business answers a published review, once. It cannot change the review or the answer afterwards.</summary>
    public async Task<Result<PartnerReview>> ReplyAsync(
        Guid businessId, Guid reviewId, Guid ownerUserId, ReplyInput input, CancellationToken cancellationToken)
    {
        if (!await OwnsBusinessAsync(businessId, ownerUserId, cancellationToken))
        {
            return ReviewErrors.NotFound;
        }

        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == reviewId && r.BusinessId == businessId, cancellationToken);
        if (review is null)
        {
            return ReviewErrors.NotFound;
        }

        var errors = new FieldErrors();
        var text = errors.Required("text", input.Text, Review.MaxReplyLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        if (review.Status != ReviewStatus.Published || review.HasReply)
        {
            return ReviewErrors.CannotReply;
        }

        db.ExpectVersion(review, input.Version);
        review.Reply(ownerUserId, text, time.GetUtcNow());
        audit.Record(ownerUserId, AuditActions.ReviewReplied, AuditEntityTypes.Review, review.Id, new { review.BusinessId });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            db.ClearTracked();
            return ReviewErrors.Changed;
        }

        return ToPartner(review, await ListingTitlesAsync([review.ListingId], cancellationToken));
    }

    // --- Moderators ---

    /// <summary>The moderators' queue: waiting reviews oldest first by default, or any other state newest first.</summary>
    public async Task<PagedResult<AdminReview>> AdminListAsync(AdminReviewQuery query, CancellationToken cancellationToken)
    {
        var status = query.Status ?? ReviewStatus.Pending;
        var (p, size) = Paging.Normalize(query.Page, query.PageSize);
        var reviews = db.Reviews.AsNoTracking().Where(r => r.Status == status);
        var total = await reviews.CountAsync(cancellationToken);
        var ordered = status == ReviewStatus.Pending
            ? reviews.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            : reviews.OrderByDescending(r => r.ModeratedAt).ThenByDescending(r => r.Id);
        var items = await ordered.Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken);
        var names = await BusinessNamesAsync(items.Select(r => r.BusinessId), cancellationToken);
        var titles = await ListingTitlesAsync(items.Select(r => r.ListingId), cancellationToken);
        return new PagedResult<AdminReview>([.. items.Select(r => ToAdmin(r, names, titles))], p, size, total);
    }

    public Task<Result<AdminReview>> PublishAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        ModerateAsync(id, version, staffUserId, ReviewStatus.Pending, AuditActions.ReviewPublished, null, (review, now) => review.Publish(staffUserId, now), cancellationToken);

    public Task<Result<AdminReview>> RejectAsync(Guid id, ModerationInput input, Guid staffUserId, CancellationToken cancellationToken) =>
        ModerateWithReasonAsync(id, input, staffUserId, ReviewStatus.Pending, AuditActions.ReviewRejected, (review, reason, now) => review.Reject(staffUserId, reason, now), cancellationToken);

    public Task<Result<AdminReview>> RemoveAsync(Guid id, ModerationInput input, Guid staffUserId, CancellationToken cancellationToken) =>
        ModerateWithReasonAsync(id, input, staffUserId, ReviewStatus.Published, AuditActions.ReviewRemoved, (review, reason, now) => review.Remove(staffUserId, reason, now), cancellationToken);

    private async Task<Result<AdminReview>> ModerateWithReasonAsync(
        Guid id,
        ModerationInput input,
        Guid staffUserId,
        ReviewStatus requiredStatus,
        string action,
        Action<Review, string, DateTimeOffset> apply,
        CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var reason = errors.Required("reason", input.Reason, Review.MaxReasonLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        return await ModerateAsync(id, input.Version, staffUserId, requiredStatus, action, reason, (review, now) => apply(review, reason, now), cancellationToken);
    }

    private async Task<Result<AdminReview>> ModerateAsync(
        Guid id,
        uint version,
        Guid staffUserId,
        ReviewStatus requiredStatus,
        string action,
        string? reason,
        Action<Review, DateTimeOffset> apply,
        CancellationToken cancellationToken)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (review is null)
        {
            return ReviewErrors.NotFound;
        }

        if (review.Status != requiredStatus)
        {
            return ReviewErrors.WrongState;
        }

        db.ExpectVersion(review, version);
        apply(review, time.GetUtcNow());
        audit.Record(staffUserId, action, AuditEntityTypes.Review, review.Id, new { review.BusinessId, review.Rating, reason });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            db.ClearTracked();
            return ReviewErrors.Changed;
        }

        var names = await BusinessNamesAsync([review.BusinessId], cancellationToken);
        var titles = await ListingTitlesAsync([review.ListingId], cancellationToken);
        return ToAdmin(review, names, titles);
    }

    // --- Shared ---

    private static int ValidRating(int? rating, FieldErrors errors)
    {
        if (rating is >= Review.MinRating and <= Review.MaxRating)
        {
            return rating.Value;
        }

        errors.Add("rating", $"Choose a rating from {Review.MinRating} to {Review.MaxRating}.");
        return Review.MinRating;
    }

    /// <summary>The first name the customer gave. A name that is really a number, or none at all, shows as "A customer".</summary>
    private async Task<string> AuthorNameAsync(Guid customerUserId, CancellationToken cancellationToken)
    {
        var profile = await accounts.GetProfileAsync(customerUserId, cancellationToken);
        var first = profile?.DisplayName?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(first) || first.Count(char.IsDigit) >= 4 || first.Contains('@'))
        {
            return AnonymousAuthor;
        }

        return first.Length > Review.MaxAuthorNameLength ? first[..Review.MaxAuthorNameLength] : first;
    }

    private async Task<bool> OwnsBusinessAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken) =>
        await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId && b.OwnerUserId == ownerUserId, cancellationToken);

    private async Task<Dictionary<Guid, string>> BusinessNamesAsync(IEnumerable<Guid> businessIds, CancellationToken cancellationToken)
    {
        var ids = businessIds.Distinct().ToList();
        return await db.Businesses.AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> ListingTitlesAsync(IEnumerable<Guid?> listingIds, CancellationToken cancellationToken)
    {
        var ids = listingIds.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await db.Listings.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Title, cancellationToken);
    }

    private async Task<MyReview> ToMineAsync(Review review, CancellationToken cancellationToken)
    {
        var names = await BusinessNamesAsync([review.BusinessId], cancellationToken);
        return ToMine(review, names.GetValueOrDefault(review.BusinessId, string.Empty), options.Value, time.GetUtcNow());
    }

    private static MyReview ToMine(Review r, string businessName, ReviewOptions settings, DateTimeOffset now) => new(
        r.Id,
        r.BookingId,
        r.FoodOrderId,
        r.BusinessId,
        businessName,
        r.Rating,
        r.Text,
        r.Status,
        r.ModerationReason,
        ToReply(r),
        r.CreatedAt,
        (r.Status is ReviewStatus.Pending or ReviewStatus.Published) && r.EditedAt is null && now <= r.CreatedAt + settings.EditWindow,
        r.Version);

    private static ReviewReply? ToReply(Review r) => r.ReplyText is { } text ? new ReviewReply(text, r.RepliedAt!.Value) : null;

    private static PartnerReview ToPartner(Review r, Dictionary<Guid, string> titles) => new(
        r.Id,
        r.BookingId is not null ? BookingKind : FoodOrderKind,
        r.ListingId,
        r.ListingId is { } listingId ? titles.GetValueOrDefault(listingId) : null,
        r.Rating,
        r.Text,
        r.AuthorName,
        r.PublishedAt!.Value,
        ToReply(r),
        r.Version);

    private static AdminReview ToAdmin(Review r, Dictionary<Guid, string> names, Dictionary<Guid, string> titles) => new(
        r.Id,
        r.BookingId is not null ? BookingKind : FoodOrderKind,
        r.BusinessId,
        names.GetValueOrDefault(r.BusinessId, string.Empty),
        r.ListingId,
        r.ListingId is { } listingId ? titles.GetValueOrDefault(listingId) : null,
        r.Rating,
        r.Text,
        r.AuthorName,
        r.Status,
        ReviewChecks.Split(r.AutoCheckFlags),
        r.ModerationReason,
        r.CreatedAt,
        r.EditedAt,
        ToReply(r),
        r.Version);
}
