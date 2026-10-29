using OnePondy.Domain.Listings;

namespace OnePondy.Domain.Bookings;

public enum BookingStatus
{
    Pending = 1,
    AwaitingPayment = 2,
    Confirmed = 3,
    Failed = 4,
    Expired = 5,
    Rejected = 6,
    Cancelled = 7,
    RefundPending = 8,
    Refunded = 9,
    Completed = 10,
}

/// <summary>Who caused a booking status change (spec §17.3, <c>booking_status_history.changed_by_type</c>).</summary>
public enum BookingActorType
{
    Customer = 1,
    Partner = 2,
    Admin = 3,
    System = 4,
}

/// <param name="UnitPrice">Price for one guest or one night, as shown to the customer at booking time.</param>
public sealed record PriceSnapshot(
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal,
    decimal Fees,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string Currency);

/// <summary>
/// A customer's reservation against a listing (spec §7, §10, Phase 6). The price and cancellation
/// policy are snapshotted at creation so a later change to the listing never rewrites history.
/// Payment capture and refunds belong to Phase 7; until then every booking is treated as free or
/// pay-at-venue, and a partner confirms it directly without an <c>awaiting_payment</c> step.
/// </summary>
public sealed class Booking
{
    private readonly List<BookingStatusHistoryEntry> _history = [];

    private Booking()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerUserId { get; private set; }

    public Guid BusinessId { get; private set; }

    public Guid ListingId { get; private set; }

    public ListingType BookingType { get; private set; }

    public BookingStatus Status { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>When the inventory hold expires; set only while <see cref="Status"/> is a holding state.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? StartAt { get; private set; }

    public DateTimeOffset? EndAt { get; private set; }

    public int GuestCount { get; private set; }

    public string Currency { get; private set; } = "INR";

    public decimal Subtotal { get; private set; }

    public decimal Fees { get; private set; }

    public decimal TaxAmount { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    /// <summary>The breakdown as the customer saw it when they booked; the scalar columns above mirror it for queries.</summary>
    public PriceSnapshot PriceSnapshot { get; private set; } = null!;

    public CancellationPolicy CancellationPolicySnapshot { get; private set; } = null!;

    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<BookingStatusHistoryEntry> History => _history;

    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(30);

    public static Booking CreateHold(
        Guid customerUserId,
        Guid businessId,
        Guid listingId,
        ListingType bookingType,
        string idempotencyKey,
        DateTimeOffset? startAt,
        DateTimeOffset? endAt,
        int guestCount,
        PriceSnapshot priceSnapshot,
        CancellationPolicy cancellationPolicySnapshot,
        DateTimeOffset now)
    {
        if (guestCount < 1)
        {
            throw new ArgumentException("Guest count must be at least 1.", nameof(guestCount));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var booking = new Booking
        {
            Id = Guid.CreateVersion7(),
            CustomerUserId = customerUserId,
            BusinessId = businessId,
            ListingId = listingId,
            BookingType = bookingType,
            Status = BookingStatus.Pending,
            IdempotencyKey = idempotencyKey,
            ExpiresAt = now + HoldDuration,
            StartAt = startAt,
            EndAt = endAt,
            GuestCount = guestCount,
            Currency = priceSnapshot.Currency,
            Subtotal = priceSnapshot.Subtotal,
            Fees = priceSnapshot.Fees,
            TaxAmount = priceSnapshot.TaxAmount,
            DiscountAmount = priceSnapshot.DiscountAmount,
            TotalAmount = priceSnapshot.TotalAmount,
            PriceSnapshot = priceSnapshot,
            CancellationPolicySnapshot = cancellationPolicySnapshot,
            CreatedAt = now,
            UpdatedAt = now,
        };
        booking.RecordHistory(null, BookingStatus.Pending, BookingActorType.Customer, customerUserId, null, now);
        return booking;
    }

    /// <summary>
    /// The partner accepts a free or pay-at-venue request, which confirms it straight away. A booking
    /// that needs online payment goes through <see cref="AcceptForPayment"/> instead.
    /// </summary>
    public void Confirm(Guid partnerUserId, DateTimeOffset now) =>
        TransitionTo(BookingStatus.Confirmed, BookingActorType.Partner, partnerUserId, null, now, BookingStatus.Pending);

    /// <summary>
    /// The partner accepts a request that must be paid for online (spec §7: pending to awaiting_payment).
    /// The seats stay held, and the customer gets a fresh window to pay that replaces the request hold.
    /// </summary>
    public void AcceptForPayment(Guid partnerUserId, DateTimeOffset now, TimeSpan paymentWindow)
    {
        if (paymentWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentWindow), "The payment window must be positive.");
        }

        TransitionTo(BookingStatus.AwaitingPayment, BookingActorType.Partner, partnerUserId, null, now, BookingStatus.Pending);
        ExpiresAt = now + paymentWindow;
    }

    /// <summary>A verified payment arrived for a booking waiting on it (spec §7: awaiting_payment to confirmed).</summary>
    public void ConfirmPayment(DateTimeOffset now) =>
        TransitionTo(BookingStatus.Confirmed, BookingActorType.System, null, null, now, BookingStatus.AwaitingPayment);

    public void Reject(Guid partnerUserId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        TransitionTo(BookingStatus.Rejected, BookingActorType.Partner, partnerUserId, reason, now, BookingStatus.Pending, BookingStatus.AwaitingPayment);
    }

    public void Cancel(BookingActorType actorType, Guid? actorUserId, string? reason, DateTimeOffset now) =>
        TransitionTo(BookingStatus.Cancelled, actorType, actorUserId, reason, now, BookingStatus.Pending, BookingStatus.Confirmed);

    /// <summary>The worker releases a hold that was never acted on in time.</summary>
    public void Expire(DateTimeOffset now) =>
        TransitionTo(BookingStatus.Expired, BookingActorType.System, null, null, now, BookingStatus.Pending, BookingStatus.AwaitingPayment);

    public void Complete(Guid actorUserId, BookingActorType actorType, DateTimeOffset now) =>
        TransitionTo(BookingStatus.Completed, actorType, actorUserId, null, now, BookingStatus.Confirmed);

    /// <summary>Phase 7: the gateway reported a verified failure.</summary>
    public void Fail(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        TransitionTo(BookingStatus.Failed, BookingActorType.System, null, reason, now, BookingStatus.AwaitingPayment);
    }

    /// <summary>Phase 7: a captured payment needs refunding after a rejection or cancellation.</summary>
    public void RequestRefund(BookingActorType actorType, Guid? actorUserId, DateTimeOffset now) =>
        TransitionTo(BookingStatus.RefundPending, actorType, actorUserId, null, now, BookingStatus.Rejected, BookingStatus.Cancelled);

    /// <summary>Phase 7: the gateway confirmed the refund.</summary>
    public void MarkRefunded(DateTimeOffset now) =>
        TransitionTo(BookingStatus.Refunded, BookingActorType.System, null, null, now, BookingStatus.RefundPending);

    /// <summary>Whether this status still holds inventory and has an expiry.</summary>
    public bool IsHolding => Status is BookingStatus.Pending or BookingStatus.AwaitingPayment;

    private void TransitionTo(
        BookingStatus next,
        BookingActorType actorType,
        Guid? actorUserId,
        string? reason,
        DateTimeOffset now,
        params ReadOnlySpan<BookingStatus> allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
        {
            throw new InvalidOperationException($"A booking cannot move from {Status} to {next}.");
        }

        var previous = Status;
        Status = next;
        if (!IsHolding)
        {
            ExpiresAt = null;
        }

        if (next == BookingStatus.Cancelled)
        {
            CancellationReason = reason;
        }

        UpdatedAt = now;
        RecordHistory(previous, next, actorType, actorUserId, reason, now);
    }

    private void RecordHistory(
        BookingStatus? previous,
        BookingStatus next,
        BookingActorType actorType,
        Guid? actorUserId,
        string? reason,
        DateTimeOffset now) =>
        _history.Add(new BookingStatusHistoryEntry(Guid.CreateVersion7(), Id, previous, next, actorType, actorUserId, reason, now));
}

public sealed class BookingStatusHistoryEntry
{
    private BookingStatusHistoryEntry()
    {
    }

    internal BookingStatusHistoryEntry(
        Guid id,
        Guid bookingId,
        BookingStatus? previousStatus,
        BookingStatus newStatus,
        BookingActorType actorType,
        Guid? actorUserId,
        string? reason,
        DateTimeOffset createdAt)
    {
        Id = id;
        BookingId = bookingId;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        ActorType = actorType;
        ActorUserId = actorUserId;
        Reason = reason;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public BookingStatus? PreviousStatus { get; private set; }

    public BookingStatus NewStatus { get; private set; }

    public BookingActorType ActorType { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
