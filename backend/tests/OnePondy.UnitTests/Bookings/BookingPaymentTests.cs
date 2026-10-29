using OnePondy.Application.Bookings;
using OnePondy.Domain.Bookings;

namespace OnePondy.UnitTests.Bookings;

/// <summary>
/// Phase 7 step 7.1: what a partner's acceptance does once online payment can be switched on, and that
/// everything that worked before still does while it is off.
/// </summary>
public class BookingPaymentTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly BookingHarness _harness = new();

    private async Task<BookingDetail> CreateAsync() =>
        (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), None)).Value!;

    private Task<Application.Common.Result<BookingDetail>> AcceptAsync(BookingDetail booking) =>
        _harness.Bookings.AcceptAsync(_harness.BusinessId, booking.Id, booking.Version, _harness.Owner, None);

    private async Task<int> ReservedAsync()
    {
        var slots = await _harness.Listings.ListPublicSlotsAsync(_harness.ListingId, null, None);
        return slots.Value!.Single().ReservedCount;
    }

    [Fact]
    public async Task While_payments_are_off_accepting_a_priced_booking_confirms_it_as_before()
    {
        var created = await CreateAsync();

        var accepted = await AcceptAsync(created);

        Assert.Equal(BookingStatus.Confirmed, accepted.Value!.Status);
        Assert.Null(accepted.Value.ExpiresAt);
    }

    [Fact]
    public async Task With_payments_on_accepting_a_priced_booking_waits_for_payment_with_a_fresh_window()
    {
        await _harness.EnablePaymentsAsync();
        var created = await CreateAsync();
        _harness.Time.Advance(TimeSpan.FromMinutes(20));
        var acceptedAt = _harness.Time.GetUtcNow();

        var accepted = await AcceptAsync(created);

        Assert.Equal(BookingStatus.AwaitingPayment, accepted.Value!.Status);
        Assert.Equal(acceptedAt + TimeSpan.FromMinutes(30), accepted.Value.ExpiresAt);
        Assert.Equal(2, await ReservedAsync());

        var last = accepted.Value.History[^1];
        Assert.Equal(BookingStatus.Pending, last.PreviousStatus);
        Assert.Equal(BookingStatus.AwaitingPayment, last.NewStatus);
        Assert.Equal(BookingActorType.Partner, last.ActorType);
        Assert.Equal(_harness.Owner, last.ActorUserId);
    }

    [Fact]
    public async Task With_payments_on_a_free_listing_is_still_confirmed_directly()
    {
        await _harness.EnablePaymentsAsync();
        var free = await _harness.NewListingInputAsync(0m, "idem-free");
        var created = (await _harness.Bookings.CreateAsync(_harness.Customer, free, None)).Value!;

        var accepted = await _harness.Bookings.AcceptAsync(_harness.BusinessId, created.Id, created.Version, _harness.Owner, None);

        Assert.Equal(0m, created.PriceSnapshot.TotalAmount);
        Assert.Equal(BookingStatus.Confirmed, accepted.Value!.Status);
    }

    [Fact]
    public async Task The_payment_window_replaces_the_request_hold_instead_of_adding_to_it()
    {
        await _harness.EnablePaymentsAsync();
        var created = await CreateAsync();
        _harness.Time.Advance(TimeSpan.FromMinutes(20));
        await AcceptAsync(created);
        var expiry = new BookingHoldExpiry(_harness.Db, _harness.Bookings, _harness.Time);

        // 35 minutes after the request: past the original hold, still inside the new payment window.
        _harness.Time.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(0, await expiry.ExpireDueHoldsAsync(None));
        Assert.Equal(2, await ReservedAsync());

        // 51 minutes after the request: the 30 minutes to pay (from acceptance at minute 20) have run out.
        _harness.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(1, await expiry.ExpireDueHoldsAsync(None));
    }

    [Fact]
    public async Task A_booking_that_is_not_paid_in_time_expires_and_releases_its_seats()
    {
        await _harness.EnablePaymentsAsync();
        var created = await CreateAsync();
        await AcceptAsync(created);
        _harness.Time.Advance(TimeSpan.FromMinutes(31));

        var expired = await new BookingHoldExpiry(_harness.Db, _harness.Bookings, _harness.Time).ExpireDueHoldsAsync(None);

        Assert.Equal(1, expired);
        var detail = (await _harness.Bookings.GetMineAsync(created.Id, _harness.Customer, None)).Value!;
        Assert.Equal(BookingStatus.Expired, detail.Status);
        Assert.Null(detail.ExpiresAt);
        Assert.Equal(BookingActorType.System, detail.History[^1].ActorType);
        Assert.Equal(0, await ReservedAsync());
    }

    [Fact]
    public async Task A_partner_can_still_reject_a_pending_request_and_the_seats_are_released()
    {
        await _harness.EnablePaymentsAsync();
        var created = await CreateAsync();

        var rejected = await _harness.Bookings.RejectAsync(_harness.BusinessId, created.Id, created.Version, _harness.Owner, "Fully booked", None);

        Assert.Equal(BookingStatus.Rejected, rejected.Value!.Status);
        Assert.Equal(0, await ReservedAsync());
    }

    [Fact]
    public async Task A_booking_waiting_for_payment_can_be_rejected_which_releases_the_seats()
    {
        await _harness.EnablePaymentsAsync();
        var accepted = (await AcceptAsync(await CreateAsync())).Value!;

        var rejected = await _harness.Bookings.RejectAsync(_harness.BusinessId, accepted.Id, accepted.Version, _harness.Owner, "Cannot host", None);

        Assert.Equal(BookingStatus.Rejected, rejected.Value!.Status);
        Assert.Equal(0, await ReservedAsync());
    }

    [Fact]
    public async Task A_customer_can_walk_away_from_a_booking_that_is_waiting_for_payment()
    {
        await _harness.EnablePaymentsAsync();
        var accepted = (await AcceptAsync(await CreateAsync())).Value!;

        var cancelled = await _harness.Bookings.CancelMineAsync(accepted.Id, _harness.Customer, "Changed my mind", accepted.Version, None);

        Assert.Equal(BookingStatus.Cancelled, cancelled.Value!.Status);
        Assert.Null(cancelled.Value.ExpiresAt);
        Assert.Equal(BookingActorType.Customer, cancelled.Value.History[^1].ActorType);
        Assert.Equal(BookingStatus.AwaitingPayment, cancelled.Value.History[^1].PreviousStatus);
        Assert.Equal(0, await ReservedAsync());
    }

    [Fact]
    public async Task A_partner_rejects_rather_than_cancels_a_booking_that_is_waiting_for_payment()
    {
        await _harness.EnablePaymentsAsync();
        var accepted = (await AcceptAsync(await CreateAsync())).Value!;

        var byPartner = await _harness.Bookings.CancelForBusinessAsync(_harness.BusinessId, accepted.Id, accepted.Version, _harness.Owner, null, None);

        Assert.Equal("booking_invalid_transition", byPartner.Error!.Code);
        Assert.Equal(2, await ReservedAsync());
    }

    [Fact]
    public async Task A_booking_that_was_already_accepted_cannot_be_accepted_again()
    {
        await _harness.EnablePaymentsAsync();
        var created = await CreateAsync();
        await AcceptAsync(created);

        var again = await AcceptAsync(created);

        Assert.Equal("booking_invalid_transition", again.Error!.Code);
    }
}
