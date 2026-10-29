using OnePondy.Domain.Bookings;

namespace OnePondy.UnitTests.Bookings;

public class BookingServiceTests
{
    private readonly BookingHarness _harness = new();

    [Fact]
    public async Task Creating_a_booking_holds_the_slot_and_starts_pending()
    {
        var result = await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Pending, result.Value!.Status);
        Assert.Equal(1000m, result.Value.PriceSnapshot.TotalAmount);

        var slot = await _harness.Listings.ListPublicSlotsAsync(_harness.ListingId, null, default);
        Assert.Equal(2, slot.Value!.Single().ReservedCount);
    }

    [Fact]
    public async Task Retrying_the_same_idempotency_key_returns_the_original_booking()
    {
        var first = await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default);
        var second = await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default);

        Assert.Equal(first.Value!.Id, second.Value!.Id);

        var slot = await _harness.Listings.ListPublicSlotsAsync(_harness.ListingId, null, default);
        Assert.Equal(2, slot.Value!.Single().ReservedCount);
    }

    [Fact]
    public async Task Booking_more_guests_than_the_slot_has_room_for_fails()
    {
        var result = await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(guestCount: 5), default);

        Assert.Equal("not_enough_availability", result.Error!.Code);
    }

    [Fact]
    public async Task A_draft_listing_cannot_be_booked()
    {
        var draftInput = new OnePondy.Application.Listings.ListingApplicationInput(
            OnePondy.Domain.Listings.ListingType.Activity, "Unfinished tour", null, true, 300m, 12, 50, null);
        var draft = await _harness.Listings.CreateAsync(_harness.BusinessId, _harness.Owner, draftInput, default);
        var start = _harness.Time.GetUtcNow().AddDays(1);
        var slot = await _harness.Listings.CreateSlotAsync(draft.Value!.Id, _harness.Owner, start, start.AddHours(1), 5, default);

        var result = await _harness.Bookings.CreateAsync(
            _harness.Customer,
            new OnePondy.Application.Bookings.CreateBookingInput(draft.Value.Id, [slot.Value!.Id], 1, "idem-draft"),
            default);

        Assert.Equal("listing_not_bookable", result.Error!.Code);
    }

    [Fact]
    public async Task The_partner_can_accept_then_complete_a_pending_booking()
    {
        var created = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default)).Value!;

        var accepted = await _harness.Bookings.AcceptAsync(_harness.BusinessId, created.Id, created.Version, _harness.Owner, default);
        Assert.Equal(BookingStatus.Confirmed, accepted.Value!.Status);

        var completed = await _harness.Bookings.CompleteAsync(_harness.BusinessId, created.Id, accepted.Value.Version, _harness.Owner, default);
        Assert.Equal(BookingStatus.Completed, completed.Value!.Status);
    }

    [Fact]
    public async Task Rejecting_a_pending_booking_releases_its_slot_and_needs_a_reason()
    {
        var created = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default)).Value!;

        var blank = await _harness.Bookings.RejectAsync(_harness.BusinessId, created.Id, created.Version, _harness.Owner, null, default);
        Assert.Equal("validation_failed", blank.Error!.Code);

        var rejected = await _harness.Bookings.RejectAsync(_harness.BusinessId, created.Id, created.Version, _harness.Owner, "Fully booked elsewhere", default);
        Assert.Equal(BookingStatus.Rejected, rejected.Value!.Status);

        var slot = await _harness.Listings.ListPublicSlotsAsync(_harness.ListingId, null, default);
        Assert.Equal(0, slot.Value!.Single().ReservedCount);
    }

    [Fact]
    public async Task The_customer_can_cancel_their_own_pending_booking_but_not_someone_elses()
    {
        var created = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default)).Value!;
        var stranger = Guid.NewGuid();

        var denied = await _harness.Bookings.CancelMineAsync(created.Id, stranger, null, created.Version, default);
        Assert.Equal("booking_not_found", denied.Error!.Code);

        var cancelled = await _harness.Bookings.CancelMineAsync(created.Id, _harness.Customer, "Change of plans", created.Version, default);
        Assert.Equal(BookingStatus.Cancelled, cancelled.Value!.Status);

        var slot = await _harness.Listings.ListPublicSlotsAsync(_harness.ListingId, null, default);
        Assert.Equal(0, slot.Value!.Single().ReservedCount);
    }

    [Fact]
    public async Task Cancelling_a_confirmed_booking_releases_its_slot_for_either_side()
    {
        var byCustomer = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default)).Value!;
        var accepted = (await _harness.Bookings.AcceptAsync(_harness.BusinessId, byCustomer.Id, byCustomer.Version, _harness.Owner, default)).Value!;
        var cancelled = await _harness.Bookings.CancelMineAsync(accepted.Id, _harness.Customer, "Change of plans", accepted.Version, default);

        Assert.Equal(BookingStatus.Cancelled, cancelled.Value!.Status);
        var slot = await _harness.Listings.ListPublicSlotsAsync(_harness.ListingId, null, default);
        Assert.Equal(0, slot.Value!.Single().ReservedCount);

        var byPartner = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(idempotencyKey: "idem-2"), default)).Value!;
        var confirmed = (await _harness.Bookings.AcceptAsync(_harness.BusinessId, byPartner.Id, byPartner.Version, _harness.Owner, default)).Value!;
        await _harness.Bookings.CancelForBusinessAsync(_harness.BusinessId, confirmed.Id, confirmed.Version, _harness.Owner, "Closed that day", default);

        slot = await _harness.Listings.ListPublicSlotsAsync(_harness.ListingId, null, default);
        Assert.Equal(0, slot.Value!.Single().ReservedCount);
    }

    [Fact]
    public async Task A_different_business_cannot_see_or_act_on_another_partners_bookings()
    {
        var created = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default)).Value!;

        var result = await _harness.Bookings.AcceptAsync(Guid.NewGuid(), created.Id, created.Version, _harness.Owner, default);

        Assert.Equal("booking_not_found", result.Error!.Code);
    }

    [Fact]
    public async Task A_slot_that_was_ever_booked_cannot_be_removed_even_after_the_booking_is_cancelled()
    {
        var created = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default)).Value!;
        await _harness.Bookings.CancelMineAsync(created.Id, _harness.Customer, null, created.Version, default);

        var removed = await _harness.Listings.DeleteSlotAsync(_harness.ListingId, _harness.SlotId, _harness.Owner, default);

        Assert.Equal("slot_has_reservations", removed!.Code);
    }

    [Fact]
    public async Task Expiring_an_overdue_hold_releases_its_slot()
    {
        var created = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default)).Value!;
        _harness.Time.Advance(OnePondy.Domain.Bookings.Booking.HoldDuration + TimeSpan.FromMinutes(1));

        var expired = await _harness.Bookings.TryExpireAsync(created.Id, default);

        Assert.True(expired);
        var detail = await _harness.Bookings.GetMineAsync(created.Id, _harness.Customer, default);
        Assert.Equal(BookingStatus.Expired, detail.Value!.Status);
        var slot = await _harness.Listings.ListPublicSlotsAsync(_harness.ListingId, null, default);
        Assert.Equal(0, slot.Value!.Single().ReservedCount);
    }

    [Fact]
    public async Task Expiring_a_booking_that_is_not_holding_anything_does_nothing()
    {
        var created = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default)).Value!;
        await _harness.Bookings.AcceptAsync(_harness.BusinessId, created.Id, created.Version, _harness.Owner, default);

        var expired = await _harness.Bookings.TryExpireAsync(created.Id, default);

        Assert.False(expired);
    }
}
