using OnePondy.Domain.Bookings;
using OnePondy.Domain.Listings;

namespace OnePondy.UnitTests.Domain;

public class BookingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid ListingId = Guid.NewGuid();
    private static readonly Guid PartnerUserId = Guid.NewGuid();

    private static Booking Hold(DateTimeOffset? now = null) => Booking.CreateHold(
        CustomerId,
        BusinessId,
        ListingId,
        ListingType.Activity,
        "idem-1",
        Now,
        Now.AddHours(2),
        2,
        new PriceSnapshot(500m, 2, 1000m, 0m, 0m, 0m, 1000m, "INR"),
        new CancellationPolicy(24, 50, null),
        now ?? Now);

    [Fact]
    public void A_new_hold_is_pending_with_an_expiry_and_one_history_entry()
    {
        var booking = Hold();

        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.True(booking.IsHolding);
        Assert.Equal(Now + Booking.HoldDuration, booking.ExpiresAt);
        var entry = Assert.Single(booking.History);
        Assert.Null(entry.PreviousStatus);
        Assert.Equal(BookingStatus.Pending, entry.NewStatus);
        Assert.Equal(BookingActorType.Customer, entry.ActorType);
    }

    [Fact]
    public void Confirming_a_pending_booking_clears_its_hold_expiry()
    {
        var booking = Hold();

        booking.Confirm(PartnerUserId, Now.AddMinutes(5));

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.False(booking.IsHolding);
        Assert.Null(booking.ExpiresAt);
        Assert.Equal(2, booking.History.Count);
    }

    [Fact]
    public void A_confirmed_booking_cannot_be_rejected_or_confirmed_again()
    {
        var booking = Hold();
        booking.Confirm(PartnerUserId, Now);

        Assert.Throws<InvalidOperationException>(() => booking.Reject(PartnerUserId, "Too late", Now));
        Assert.Throws<InvalidOperationException>(() => booking.Confirm(PartnerUserId, Now));
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_records_it()
    {
        var booking = Hold();

        booking.Reject(PartnerUserId, "Fully booked elsewhere", Now.AddMinutes(1));

        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.Equal("Fully booked elsewhere", booking.History[^1].Reason);
        Assert.Throws<ArgumentException>(() => Hold().Reject(PartnerUserId, " ", Now));
    }

    [Fact]
    public void A_confirmed_booking_can_be_cancelled_but_not_rejected_again_afterwards()
    {
        var booking = Hold();
        booking.Confirm(PartnerUserId, Now);

        booking.Cancel(BookingActorType.Customer, CustomerId, "Change of plans", Now.AddHours(1));

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal("Change of plans", booking.CancellationReason);
        Assert.Throws<InvalidOperationException>(() => booking.Complete(PartnerUserId, BookingActorType.Partner, Now));
    }

    [Fact]
    public void A_completed_or_expired_booking_is_final()
    {
        var completed = Hold();
        completed.Confirm(PartnerUserId, Now);
        completed.Complete(PartnerUserId, BookingActorType.Partner, Now.AddHours(3));
        Assert.Throws<InvalidOperationException>(() => completed.Cancel(BookingActorType.Customer, CustomerId, null, Now));

        var expired = Hold();
        expired.Expire(Now + Booking.HoldDuration);
        Assert.Equal(BookingStatus.Expired, expired.Status);
        Assert.False(expired.IsHolding);
        Assert.Throws<InvalidOperationException>(() => expired.Confirm(PartnerUserId, Now));
    }

    [Fact]
    public void Every_transition_is_appended_to_history_in_order()
    {
        var booking = Hold();
        booking.Confirm(PartnerUserId, Now.AddMinutes(1));
        booking.Complete(PartnerUserId, BookingActorType.Partner, Now.AddHours(3));

        Assert.Equal(
            [BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.Completed],
            booking.History.Select(h => h.NewStatus));
    }
}
