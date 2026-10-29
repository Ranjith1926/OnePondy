using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Listings;

namespace OnePondy.Application.Listings;

public sealed record ListingListQuery(Guid? BusinessId, ListingStatus? Status, int? Page, int? PageSize);

/// <summary>
/// Listings and their availability (spec §10, Phase 6): what a partner's approved, active business
/// sells or offers. Shops are discovery-only (D13); activities, stays, events and services can be
/// bookable, with slots a customer reserves through <c>BookingService</c>.
/// </summary>
public sealed class ListingService(IOnePondyDbContext db, AuditTrail audit, TimeProvider time)
{
    public const int MaxReasonLength = 1000;

    public async Task<Result<IReadOnlyList<ListingSummary>>> ListMineAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (!await OwnsBusinessAsync(businessId, ownerUserId, cancellationToken))
        {
            return ListingErrors.NotFound;
        }

        var listings = await db.Listings
            .AsNoTracking()
            .Where(l => l.BusinessId == businessId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);
        IReadOnlyList<ListingSummary> summaries = [.. listings.Select(ToSummary)];
        return Result<IReadOnlyList<ListingSummary>>.Success(summaries);
    }

    public async Task<Result<ListingDetail>> GetMineAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (listing is null || !await OwnsBusinessAsync(listing.BusinessId, ownerUserId, cancellationToken))
        {
            return ListingErrors.NotFound;
        }

        return ToDetail(listing);
    }

    public async Task<Result<ListingDetail>> CreateAsync(Guid businessId, Guid ownerUserId, ListingApplicationInput input, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(b => b.Id == businessId, cancellationToken);
        if (business is null || business.OwnerUserId != ownerUserId)
        {
            return ListingErrors.NotFound;
        }

        if (!business.IsActive)
        {
            return ListingErrors.BusinessNotActive;
        }

        var content = ListingInputValidator.Validate(input);
        if (!content.IsSuccess)
        {
            return content.Error;
        }

        var listing = Listing.Create(businessId, content.Value, time.GetUtcNow());
        db.Listings.Add(listing);
        audit.Record(ownerUserId, AuditActions.ListingCreated, AuditEntityTypes.Listing, listing.Id, new { listing.Title, listing.ListingType });
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(listing);
    }

    public async Task<Result<ListingDetail>> UpdateAsync(
        Guid id,
        uint expectedVersion,
        Guid ownerUserId,
        ListingApplicationInput input,
        CancellationToken cancellationToken)
    {
        var listing = await db.Listings.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (listing is null || !await OwnsBusinessAsync(listing.BusinessId, ownerUserId, cancellationToken))
        {
            return ListingErrors.NotFound;
        }

        if (!listing.CanEdit)
        {
            return ListingErrors.CannotEdit;
        }

        var content = ListingInputValidator.Validate(input);
        if (!content.IsSuccess)
        {
            return content.Error;
        }

        db.ExpectVersion(listing, expectedVersion);
        listing.UpdateContent(content.Value, time.GetUtcNow());
        audit.Record(ownerUserId, AuditActions.ListingUpdated, AuditEntityTypes.Listing, listing.Id);
        return await SaveAsync(listing, cancellationToken);
    }

    public async Task<Result<ListingDetail>> SubmitAsync(Guid id, uint expectedVersion, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (listing is null || !await OwnsBusinessAsync(listing.BusinessId, ownerUserId, cancellationToken))
        {
            return ListingErrors.NotFound;
        }

        var business = await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == listing.BusinessId, cancellationToken);
        if (!business.IsActive)
        {
            return ListingErrors.BusinessNotActive;
        }

        db.ExpectVersion(listing, expectedVersion);
        try
        {
            listing.Submit(time.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return ListingErrors.InvalidTransition(exception.Message);
        }

        audit.Record(ownerUserId, AuditActions.ListingSubmitted, AuditEntityTypes.Listing, listing.Id);
        return await SaveAsync(listing, cancellationToken);
    }

    public async Task<PagedResult<ListingSummary>> AdminListAsync(ListingListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var listings = db.Listings.AsNoTracking();
        if (query.BusinessId is { } businessId)
        {
            listings = listings.Where(l => l.BusinessId == businessId);
        }

        if (query.Status is { } status)
        {
            listings = listings.Where(l => l.Status == status);
        }

        var total = await listings.CountAsync(cancellationToken);
        var items = await listings
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<ListingSummary>([.. items.Select(ToSummary)], page, pageSize, total);
    }

    public async Task<Result<ListingDetail>> AdminGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        return listing is null ? ListingErrors.NotFound : ToDetail(listing);
    }

    public Task<Result<ListingDetail>> PublishAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, version, staffUserId, AuditActions.ListingPublished, null, listing =>
        {
            listing.Publish(time.GetUtcNow());
            return null;
        }, cancellationToken, requireActiveBusiness: true);

    public Task<Result<ListingDetail>> SendBackAsync(Guid id, uint version, string? reason, Guid staffUserId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, MaxReasonLength);
        return errors.Any
            ? Task.FromResult(Result<ListingDetail>.Failure(errors.ToError()))
            : TransitionAsync(id, version, staffUserId, AuditActions.ListingSentBack, new { reason = trimmed }, listing =>
            {
                listing.SendBackToDraft(trimmed, time.GetUtcNow());
                return null;
            }, cancellationToken);
    }

    public Task<Result<ListingDetail>> SuspendAsync(Guid id, uint version, string? reason, Guid staffUserId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, MaxReasonLength);
        return errors.Any
            ? Task.FromResult(Result<ListingDetail>.Failure(errors.ToError()))
            : TransitionAsync(id, version, staffUserId, AuditActions.ListingSuspended, new { reason = trimmed }, listing =>
            {
                listing.Suspend(trimmed, time.GetUtcNow());
                return null;
            }, cancellationToken);
    }

    public Task<Result<ListingDetail>> ReinstateAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, version, staffUserId, AuditActions.ListingReinstated, null, listing =>
        {
            listing.Reinstate(time.GetUtcNow());
            return null;
        }, cancellationToken, requireActiveBusiness: true);

    public async Task<Result<PagedResult<PublicListingSummary>>> ListPublicAsync(ListingType? type, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var query =
            from listing in db.Listings.AsNoTracking()
            join business in db.Businesses.AsNoTracking() on listing.BusinessId equals business.Id
            where listing.Status == ListingStatus.Published && business.IsActive
            select new { listing, business };

        if (type is { } listingType)
        {
            query = query.Where(x => x.listing.ListingType == listingType);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.listing.CreatedAt)
            .Skip((p - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        return new PagedResult<PublicListingSummary>(
            [.. items.Select(x => ToPublicSummary(x.listing, x.business))], p, size, total);
    }

    public async Task<Result<PublicListingDetail>> GetPublicAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await (
            from listing in db.Listings.AsNoTracking()
            join business in db.Businesses.AsNoTracking() on listing.BusinessId equals business.Id
            where listing.Id == id
            select new { listing, business }).SingleOrDefaultAsync(cancellationToken);

        if (found is null || found.listing.Status != ListingStatus.Published || !found.business.IsActive)
        {
            return ListingErrors.NotFound;
        }

        return ToPublicDetail(found.listing, found.business);
    }

    // --- Availability slots ---

    public async Task<Result<AvailabilitySlotView>> CreateSlotAsync(
        Guid listingId,
        Guid ownerUserId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        int capacity,
        CancellationToken cancellationToken)
    {
        var listing = await FindOwnedListingAsync(listingId, ownerUserId, cancellationToken);
        if (listing is null)
        {
            return ListingErrors.NotFound;
        }

        AvailabilitySlot slot;
        try
        {
            slot = AvailabilitySlot.Create(listingId, startAt, endAt, capacity, time.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["capacity"] = [exception.Message] });
        }

        db.AvailabilitySlots.Add(slot);
        await db.SaveChangesAsync(cancellationToken);
        return ToSlotView(slot);
    }

    public async Task<Result<IReadOnlyList<AvailabilitySlotView>>> ListSlotsAsync(
        Guid listingId,
        Guid ownerUserId,
        DateTimeOffset? from,
        CancellationToken cancellationToken)
    {
        var listing = await FindOwnedListingAsync(listingId, ownerUserId, cancellationToken);
        if (listing is null)
        {
            return ListingErrors.NotFound;
        }

        return Result<IReadOnlyList<AvailabilitySlotView>>.Success(await PublicSlotsAsync(listingId, from, cancellationToken));
    }

    /// <summary>Live availability for a published, bookable listing; used by both the console and the app.</summary>
    public async Task<Result<IReadOnlyList<AvailabilitySlotView>>> ListPublicSlotsAsync(Guid listingId, DateTimeOffset? from, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listingId, cancellationToken);
        if (listing is null || listing.Status != ListingStatus.Published || !listing.IsBookable)
        {
            return ListingErrors.NotFound;
        }

        return Result<IReadOnlyList<AvailabilitySlotView>>.Success(await PublicSlotsAsync(listingId, from, cancellationToken));
    }

    public async Task<Result<AvailabilitySlotView>> UpdateSlotCapacityAsync(
        Guid listingId,
        Guid slotId,
        uint expectedVersion,
        Guid ownerUserId,
        int capacity,
        CancellationToken cancellationToken)
    {
        var listing = await FindOwnedListingAsync(listingId, ownerUserId, cancellationToken);
        if (listing is null)
        {
            return ListingErrors.NotFound;
        }

        var slot = await db.AvailabilitySlots.SingleOrDefaultAsync(s => s.Id == slotId && s.ListingId == listingId, cancellationToken);
        if (slot is null)
        {
            return ListingErrors.SlotNotFound;
        }

        db.ExpectVersion(slot, expectedVersion);
        try
        {
            slot.UpdateCapacity(capacity, time.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["capacity"] = [exception.Message] });
        }
        catch (InvalidOperationException exception)
        {
            return ListingErrors.InvalidTransition(exception.Message);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ListingErrors.SlotChanged;
        }

        return ToSlotView(slot);
    }

    public async Task<AppError?> DeleteSlotAsync(Guid listingId, Guid slotId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var listing = await FindOwnedListingAsync(listingId, ownerUserId, cancellationToken);
        if (listing is null)
        {
            return ListingErrors.NotFound;
        }

        var slot = await db.AvailabilitySlots.SingleOrDefaultAsync(s => s.Id == slotId && s.ListingId == listingId, cancellationToken);
        if (slot is null)
        {
            return ListingErrors.SlotNotFound;
        }

        // Places still held, or any booking that ever used this slot, even a cancelled one: its
        // history must keep pointing at the slot (the database refuses the delete otherwise).
        if (slot.ReservedCount > 0 || await db.BookingSlots.AsNoTracking().AnyAsync(bs => bs.SlotId == slotId, cancellationToken))
        {
            return ListingErrors.SlotHasReservations;
        }

        db.AvailabilitySlots.Remove(slot);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<IReadOnlyList<AvailabilitySlotView>> PublicSlotsAsync(Guid listingId, DateTimeOffset? from, CancellationToken cancellationToken)
    {
        var slots = db.AvailabilitySlots.AsNoTracking().Where(s => s.ListingId == listingId);
        if (from is { } cutoff)
        {
            slots = slots.Where(s => s.StartAt >= cutoff);
        }

        var items = await slots.OrderBy(s => s.StartAt).ToListAsync(cancellationToken);
        return [.. items.Select(ToSlotView)];
    }

    private async Task<Listing?> FindOwnedListingAsync(Guid listingId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.SingleOrDefaultAsync(l => l.Id == listingId, cancellationToken);
        return listing is not null && await OwnsBusinessAsync(listing.BusinessId, ownerUserId, cancellationToken) ? listing : null;
    }

    private async Task<bool> OwnsBusinessAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken) =>
        await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId && b.OwnerUserId == ownerUserId, cancellationToken);

    private async Task<Result<ListingDetail>> TransitionAsync(
        Guid id,
        uint version,
        Guid userId,
        string auditAction,
        object? auditDetails,
        Func<Listing, AppError?> apply,
        CancellationToken cancellationToken,
        bool requireActiveBusiness = false)
    {
        var listing = await db.Listings.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (listing is null)
        {
            return ListingErrors.NotFound;
        }

        if (listing.Version != version)
        {
            return ListingErrors.Changed;
        }

        if (requireActiveBusiness)
        {
            var business = await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == listing.BusinessId, cancellationToken);
            if (!business.IsActive)
            {
                return ListingErrors.BusinessNotActive;
            }
        }

        db.ExpectVersion(listing, version);
        try
        {
            if (apply(listing) is { } error)
            {
                return error;
            }
        }
        catch (InvalidOperationException exception)
        {
            return ListingErrors.InvalidTransition(exception.Message);
        }

        audit.Record(userId, auditAction, AuditEntityTypes.Listing, listing.Id, auditDetails);
        return await SaveAsync(listing, cancellationToken);
    }

    private async Task<Result<ListingDetail>> SaveAsync(Listing listing, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ListingErrors.Changed;
        }

        return ToDetail(listing);
    }

    private static ListingSummary ToSummary(Listing listing) => new(
        listing.Id,
        listing.BusinessId,
        listing.ListingType,
        listing.Title,
        listing.IsBookable,
        listing.BasePrice,
        listing.Currency,
        listing.Status,
        listing.CreatedAt,
        listing.UpdatedAt);

    private static ListingDetail ToDetail(Listing listing) => new(
        listing.Id,
        listing.Version,
        listing.BusinessId,
        listing.ListingType,
        listing.Title,
        listing.Description,
        listing.IsBookable,
        listing.BasePrice,
        listing.Currency,
        listing.CancellationPolicy,
        listing.Status,
        listing.ReviewNote,
        listing.CreatedAt,
        listing.UpdatedAt);

    private static PublicListingSummary ToPublicSummary(Listing listing, Business business) => new(
        listing.Id,
        business.Name,
        listing.ListingType,
        listing.Title,
        listing.IsBookable,
        listing.BasePrice,
        listing.Currency);

    private static PublicListingDetail ToPublicDetail(Listing listing, Business business) => new(
        listing.Id,
        business.Id,
        business.Name,
        business.Address,
        listing.ListingType,
        listing.Title,
        listing.Description,
        listing.IsBookable,
        listing.BasePrice,
        listing.Currency,
        listing.CancellationPolicy);

    private static AvailabilitySlotView ToSlotView(AvailabilitySlot slot) => new(
        slot.Id,
        slot.ListingId,
        slot.Version,
        slot.StartAt,
        slot.EndAt,
        slot.Capacity,
        slot.ReservedCount,
        slot.AvailableCount);
}
