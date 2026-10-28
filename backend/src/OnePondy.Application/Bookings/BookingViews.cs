using OnePondy.Domain.Bookings;
using OnePondy.Domain.Listings;

namespace OnePondy.Application.Bookings;

public sealed record BookingSlotView(Guid SlotId, DateTimeOffset StartAt, DateTimeOffset EndAt, int Quantity);

/// <param name="ActorUserId">Null for a system change (an expired hold); present for everything else.</param>
public sealed record BookingHistoryView(
    BookingStatus? PreviousStatus,
    BookingStatus NewStatus,
    BookingActorType ActorType,
    Guid? ActorUserId,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record BookingSummary(
    Guid Id,
    Guid ListingId,
    string ListingTitle,
    Guid BusinessId,
    string BusinessName,
    ListingType BookingType,
    BookingStatus Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    int GuestCount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record BookingDetail(
    Guid Id,
    uint Version,
    Guid ListingId,
    string ListingTitle,
    Guid BusinessId,
    string BusinessName,
    ListingType BookingType,
    BookingStatus Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    int GuestCount,
    PriceSnapshot PriceSnapshot,
    CancellationPolicy CancellationPolicySnapshot,
    string? CancellationReason,
    IReadOnlyList<BookingSlotView> Slots,
    IReadOnlyList<BookingHistoryView> History,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>What the customer asks for when creating a booking; one slot id per night for a stay.</summary>
public sealed record CreateBookingInput(Guid ListingId, IReadOnlyList<Guid> SlotIds, int GuestCount, string IdempotencyKey);
