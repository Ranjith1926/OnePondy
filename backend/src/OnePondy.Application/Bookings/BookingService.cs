using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Listings;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Listings;

namespace OnePondy.Application.Bookings;

/// <summary>
/// The booking engine (spec §7, §10, Phase 6): holds inventory, confirms or releases it, and keeps a
/// full status history. Phase 6 treats every booking as free or pay-at-venue, since the payment
/// gateway is Phase 7 — a partner confirms a request directly, with no <c>awaiting_payment</c> step.
/// </summary>
public sealed class BookingService(IOnePondyDbContext db, TimeProvider time, IOptions<PaymentsOptions> payments)
{
    public const int MaxGuestCount = 50;
    public const int MaxReasonLength = 500;

    public async Task<Result<BookingDetail>> CreateAsync(Guid customerUserId, CreateBookingInput input, CancellationToken cancellationToken)
    {
        var existing = await FindByIdempotencyKeyAsync(customerUserId, input.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await ToDetailAsync(existing, cancellationToken);
        }

        if (input.GuestCount is < 1 or > MaxGuestCount || input.SlotIds.Count == 0)
        {
            return AppError.Validation(new Dictionary<string, string[]>
            {
                ["guestCount"] = ["Enter a guest count between 1 and " + MaxGuestCount + "."],
            });
        }

        var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == input.ListingId, cancellationToken);
        if (listing is null)
        {
            return ListingErrors.NotFound;
        }

        if (listing.Status != ListingStatus.Published || !listing.IsBookable)
        {
            return BookingErrors.ListingNotBookable;
        }

        var business = await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == listing.BusinessId, cancellationToken);
        if (!business.IsActive)
        {
            return BookingErrors.ListingNotBookable;
        }

        var slots = await db.AvailabilitySlots
            .Where(s => s.ListingId == listing.Id && input.SlotIds.Contains(s.Id))
            .ToListAsync(cancellationToken);
        if (slots.Count != input.SlotIds.Count)
        {
            return BookingErrors.SlotNotFound;
        }

        var now = time.GetUtcNow();
        try
        {
            foreach (var slot in slots)
            {
                slot.Reserve(input.GuestCount, now);
            }
        }
        catch (InvalidOperationException)
        {
            return BookingErrors.NotEnoughAvailability;
        }

        var unitPrice = listing.BasePrice ?? 0m;
        var subtotal = unitPrice * input.GuestCount * slots.Count;
        var priceSnapshot = new PriceSnapshot(unitPrice, input.GuestCount * slots.Count, subtotal, 0m, 0m, 0m, subtotal, listing.Currency);
        var cancellationPolicy = listing.CancellationPolicy ?? new CancellationPolicy(0, 0, null);

        var booking = Booking.CreateHold(
            customerUserId,
            listing.BusinessId,
            listing.Id,
            listing.ListingType,
            input.IdempotencyKey,
            slots.Min(s => s.StartAt),
            slots.Max(s => s.EndAt),
            input.GuestCount,
            priceSnapshot,
            cancellationPolicy,
            now);

        db.Bookings.Add(booking);
        foreach (var slot in slots)
        {
            db.BookingSlots.Add(BookingSlot.Create(booking.Id, slot.Id, input.GuestCount));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BookingErrors.NotEnoughAvailability;
        }
        catch (DbUpdateException)
        {
            var racedExisting = await FindByIdempotencyKeyAsync(customerUserId, input.IdempotencyKey, cancellationToken);
            if (racedExisting is not null)
            {
                return await ToDetailAsync(racedExisting, cancellationToken);
            }

            throw;
        }

        return await ToDetailAsync(booking, cancellationToken);
    }

    public Task<IReadOnlyList<BookingSummary>> ListMineAsync(Guid customerUserId, CancellationToken cancellationToken) =>
        SummariesAsync(db.Bookings.AsNoTracking().Where(b => b.CustomerUserId == customerUserId), cancellationToken);

    public async Task<Result<BookingDetail>> GetMineAsync(Guid id, Guid customerUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null || booking.CustomerUserId != customerUserId)
        {
            return BookingErrors.NotFound;
        }

        return await ToDetailAsync(booking, cancellationToken);
    }

    public async Task<Result<BookingDetail>> CancelMineAsync(Guid id, Guid customerUserId, string? reason, uint expectedVersion, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null || booking.CustomerUserId != customerUserId)
        {
            return BookingErrors.NotFound;
        }

        return await TransitionAsync(booking, expectedVersion, b => b.Cancel(BookingActorType.Customer, customerUserId, Trim(reason), time.GetUtcNow()), cancellationToken);
    }

    // --- Partner booking inbox ---

    public async Task<Result<IReadOnlyList<BookingSummary>>> ListForBusinessAsync(Guid businessId, Guid ownerUserId, BookingStatus? status, CancellationToken cancellationToken)
    {
        if (!await OwnsBusinessAsync(businessId, ownerUserId, cancellationToken))
        {
            return BookingErrors.NotFound;
        }

        var query = db.Bookings.AsNoTracking().Where(b => b.BusinessId == businessId);
        if (status is { } s)
        {
            query = query.Where(b => b.Status == s);
        }

        return Result<IReadOnlyList<BookingSummary>>.Success(await SummariesAsync(query, cancellationToken));
    }

    public async Task<Result<BookingDetail>> GetForBusinessAsync(Guid businessId, Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (!await OwnsBusinessAsync(businessId, ownerUserId, cancellationToken))
        {
            return BookingErrors.NotFound;
        }

        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id && b.BusinessId == businessId, cancellationToken);
        return booking is null ? BookingErrors.NotFound : await ToDetailAsync(booking, cancellationToken);
    }

    /// <summary>
    /// Accepting a priced booking while online payment is switched on moves it to <c>awaiting_payment</c>
    /// with a fresh payment window; anything free, or any booking while payments are off, is confirmed
    /// directly as before.
    /// </summary>
    public async Task<Result<BookingDetail>> AcceptAsync(Guid businessId, Guid id, uint expectedVersion, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var paymentsOpen = await db.ModuleFlags
            .AsNoTracking()
            .AnyAsync(f => f.Key == ModuleKeys.Payments && f.Status == ModuleStatus.Available, cancellationToken);
        var paymentWindow = payments.Value.PaymentWindow;

        return await ForBusinessTransitionAsync(
            businessId,
            id,
            expectedVersion,
            ownerUserId,
            b =>
            {
                if (paymentsOpen && b.TotalAmount > 0m)
                {
                    b.AcceptForPayment(ownerUserId, time.GetUtcNow(), paymentWindow);
                }
                else
                {
                    b.Confirm(ownerUserId, time.GetUtcNow());
                }
            },
            cancellationToken);
    }

    public Task<Result<BookingDetail>> RejectAsync(Guid businessId, Guid id, uint expectedVersion, Guid ownerUserId, string? reason, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, MaxReasonLength);
        return errors.Any
            ? Task.FromResult(Result<BookingDetail>.Failure(errors.ToError()))
            : ForBusinessTransitionAsync(businessId, id, expectedVersion, ownerUserId, b => b.Reject(ownerUserId, trimmed, time.GetUtcNow()), cancellationToken);
    }

    public Task<Result<BookingDetail>> CancelForBusinessAsync(Guid businessId, Guid id, uint expectedVersion, Guid ownerUserId, string? reason, CancellationToken cancellationToken) =>
        ForBusinessTransitionAsync(businessId, id, expectedVersion, ownerUserId, b => b.Cancel(BookingActorType.Partner, ownerUserId, Trim(reason), time.GetUtcNow()), cancellationToken);

    public Task<Result<BookingDetail>> CompleteAsync(Guid businessId, Guid id, uint expectedVersion, Guid ownerUserId, CancellationToken cancellationToken) =>
        ForBusinessTransitionAsync(businessId, id, expectedVersion, ownerUserId, b => b.Complete(ownerUserId, BookingActorType.Partner, time.GetUtcNow()), cancellationToken);

    private async Task<Result<BookingDetail>> ForBusinessTransitionAsync(
        Guid businessId,
        Guid id,
        uint expectedVersion,
        Guid ownerUserId,
        Action<Booking> apply,
        CancellationToken cancellationToken)
    {
        if (!await OwnsBusinessAsync(businessId, ownerUserId, cancellationToken))
        {
            return BookingErrors.NotFound;
        }

        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.Id == id && b.BusinessId == businessId, cancellationToken);
        if (booking is null)
        {
            return BookingErrors.NotFound;
        }

        return await TransitionAsync(booking, expectedVersion, apply, cancellationToken);
    }

    private async Task<Result<BookingDetail>> TransitionAsync(Booking booking, uint expectedVersion, Action<Booking> apply, CancellationToken cancellationToken)
    {
        if (booking.Version != expectedVersion)
        {
            return BookingErrors.Changed;
        }

        db.ExpectVersion(booking, expectedVersion);
        try
        {
            apply(booking);
        }
        catch (InvalidOperationException exception)
        {
            return BookingErrors.InvalidTransition(exception.Message);
        }

        db.MarkAdded(booking.History[^1]);

        // Pending, awaiting-payment and confirmed bookings all keep their seats reserved, so any move
        // to one of these ends states gives them back, including cancelling an already confirmed booking.
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Rejected or BookingStatus.Expired or BookingStatus.Failed)
        {
            await ReleaseSlotsAsync(booking.Id, cancellationToken);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BookingErrors.Changed;
        }

        return await ToDetailAsync(booking, cancellationToken);
    }

    /// <summary>Called by the worker for a hold that timed out; not reachable through any API endpoint.</summary>
    public async Task<bool> TryExpireAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null || !booking.IsHolding)
        {
            return false;
        }

        try
        {
            booking.Expire(time.GetUtcNow());
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        db.MarkAdded(booking.History[^1]);
        await ReleaseSlotsAsync(booking.Id, cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker instance already expired this booking.
            return false;
        }
    }

    private async Task ReleaseSlotsAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var bookingSlots = await db.BookingSlots.AsNoTracking().Where(bs => bs.BookingId == bookingId).ToListAsync(cancellationToken);
        var slotIds = bookingSlots.Select(bs => bs.SlotId).ToList();
        var slots = await db.AvailabilitySlots.Where(s => slotIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var bookingSlot in bookingSlots)
        {
            slots.Single(s => s.Id == bookingSlot.SlotId).Release(bookingSlot.Quantity, now);
        }
    }

    private async Task<Booking?> FindByIdempotencyKeyAsync(Guid customerUserId, string idempotencyKey, CancellationToken cancellationToken) =>
        await db.Bookings.SingleOrDefaultAsync(b => b.CustomerUserId == customerUserId && b.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task<bool> OwnsBusinessAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken) =>
        await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId && b.OwnerUserId == ownerUserId, cancellationToken);

    // Three plain queries rather than one join: EF Core cannot order or filter after projecting a
    // join into a tuple, and a booking list never spans more than a handful of listings.
    private async Task<IReadOnlyList<BookingSummary>> SummariesAsync(IQueryable<Booking> query, CancellationToken cancellationToken)
    {
        var bookings = await query.OrderByDescending(b => b.CreatedAt).ToListAsync(cancellationToken);
        var listingIds = bookings.Select(b => b.ListingId).Distinct().ToList();
        var businessIds = bookings.Select(b => b.BusinessId).Distinct().ToList();
        var listings = await db.Listings.AsNoTracking().Where(l => listingIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        var businesses = await db.Businesses.AsNoTracking().Where(b => businessIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);
        return [.. bookings.Select(b => ToSummary(b, listings[b.ListingId], businesses[b.BusinessId]))];
    }

    private async Task<BookingDetail> ToDetailAsync(Booking booking, CancellationToken cancellationToken)
    {
        var listing = await db.Listings.AsNoTracking().SingleAsync(l => l.Id == booking.ListingId, cancellationToken);
        var business = await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == booking.BusinessId, cancellationToken);
        var bookingSlots = await db.BookingSlots.AsNoTracking().Where(bs => bs.BookingId == booking.Id).ToListAsync(cancellationToken);
        var slotIds = bookingSlots.Select(bs => bs.SlotId).ToList();
        var slots = await db.AvailabilitySlots.AsNoTracking().Where(s => slotIds.Contains(s.Id)).ToListAsync(cancellationToken);

        return new BookingDetail(
            booking.Id,
            booking.Version,
            booking.ListingId,
            listing.Title,
            booking.BusinessId,
            business.Name,
            booking.BookingType,
            booking.Status,
            booking.ExpiresAt,
            booking.StartAt,
            booking.EndAt,
            booking.GuestCount,
            booking.PriceSnapshot,
            booking.CancellationPolicySnapshot,
            booking.CancellationReason,
            [.. bookingSlots.Select(bs => ToSlotView(bs, slots.Single(s => s.Id == bs.SlotId)))],
            [.. booking.History.OrderBy(h => h.CreatedAt).Select(ToHistoryView)],
            booking.CreatedAt,
            booking.UpdatedAt);
    }

    private static BookingSummary ToSummary(Booking booking, Listing listing, Business business) => new(
        booking.Id,
        booking.ListingId,
        listing.Title,
        booking.BusinessId,
        business.Name,
        booking.BookingType,
        booking.Status,
        booking.ExpiresAt,
        booking.StartAt,
        booking.EndAt,
        booking.GuestCount,
        booking.PriceSnapshot.TotalAmount,
        booking.PriceSnapshot.Currency,
        booking.CreatedAt);

    private static BookingSlotView ToSlotView(BookingSlot bookingSlot, AvailabilitySlot slot) =>
        new(slot.Id, slot.StartAt, slot.EndAt, bookingSlot.Quantity);

    private static BookingHistoryView ToHistoryView(BookingStatusHistoryEntry entry) =>
        new(entry.PreviousStatus, entry.NewStatus, entry.ActorType, entry.ActorUserId, entry.Reason, entry.CreatedAt);

    private static string? Trim(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}
