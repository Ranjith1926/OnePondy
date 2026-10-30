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
    DateTimeOffset CreatedAt,
    /// <summary>What a partner's accept, reject, cancel or complete must send back, so a stale screen cannot act on a booking that has since changed.</summary>
    uint Version);

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
    DateTimeOffset UpdatedAt,
    PaymentView? Payment,
    IReadOnlyList<RefundView> Refunds);

/// <summary>What the customer paid for a booking, for their receipt. Null while nothing has been paid online.</summary>
/// <param name="Reference">The gateway's payment id, which support can use to find it.</param>
public sealed record PaymentView(decimal Amount, string Currency, DateTimeOffset PaidAt, string Reference);

/// <summary>Money going back to the customer, and how far along it is, so they never have to wonder (spec Phase 7: refund status).</summary>
public sealed record RefundView(
    decimal Amount,
    OnePondy.Domain.Payments.RefundReason Reason,
    OnePondy.Domain.Payments.RefundStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ProcessedAt);

/// <summary>What the customer asks for when creating a booking; one slot id per night for a stay.</summary>
public sealed record CreateBookingInput(Guid ListingId, IReadOnlyList<Guid> SlotIds, int GuestCount, string IdempotencyKey);
