using OnePondy.Application.Bookings;
using OnePondy.Application.Listings;

namespace OnePondy.UnitTests.Bookings;

public class SavedListingsServiceTests
{
    private readonly BookingHarness _harness = new();

    private SavedListingsService Saved => new(_harness.Db, _harness.Time);

    [Fact]
    public async Task Saving_twice_keeps_one_bookmark_and_listing_it_shows_the_live_listing()
    {
        Assert.Null(await Saved.SaveAsync(_harness.Customer, _harness.ListingId, default));
        Assert.Null(await Saved.SaveAsync(_harness.Customer, _harness.ListingId, default));

        var saved = await Saved.ListAsync(_harness.Customer, default);

        var view = Assert.Single(saved);
        Assert.Equal("Backwater Kayaking", view.Listing.Title);
        Assert.Equal("Pondy Paddle Tours", view.Listing.BusinessName);
    }

    [Fact]
    public async Task A_listing_that_is_not_live_cannot_be_saved()
    {
        var draft = await _harness.Listings.CreateAsync(
            _harness.BusinessId,
            _harness.Owner,
            new ListingApplicationInput(OnePondy.Domain.Listings.ListingType.Activity, "Draft tour", null, false, null, null, null, null),
            default);

        var error = await Saved.SaveAsync(_harness.Customer, draft.Value!.Id, default);

        Assert.Equal("listing_not_available", error!.Code);
    }

    [Fact]
    public async Task A_saved_listing_that_gets_suspended_disappears_from_the_list()
    {
        await Saved.SaveAsync(_harness.Customer, _harness.ListingId, default);
        var listing = (await _harness.Listings.AdminGetAsync(_harness.ListingId, default)).Value!;
        await _harness.Listings.SuspendAsync(listing.Id, listing.Version, "Safety complaint", _harness.Owner, default);

        Assert.Empty(await Saved.ListAsync(_harness.Customer, default));
    }

    [Fact]
    public async Task Removing_a_bookmark_that_is_not_there_is_fine()
    {
        await Saved.RemoveAsync(_harness.Customer, _harness.ListingId, default);

        await Saved.SaveAsync(_harness.Customer, _harness.ListingId, default);
        await Saved.RemoveAsync(_harness.Customer, _harness.ListingId, default);
        Assert.Empty(await Saved.ListAsync(_harness.Customer, default));
    }
}

public class BookingsDeletionGuardTests
{
    private readonly BookingHarness _harness = new();

    private BookingsDeletionGuard Guard => new(_harness.Db);

    [Fact]
    public async Task Nothing_blocks_an_account_with_no_bookings()
    {
        Assert.Empty(await Guard.GetBlockersAsync(_harness.Customer, default));
        Assert.Empty(await Guard.GetBlockersAsync(_harness.Owner, default));
    }

    [Fact]
    public async Task A_booking_in_progress_blocks_both_the_customer_and_the_partner()
    {
        await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default);

        Assert.Equal([BookingsDeletionGuard.CustomerBookingsInProgress], await Guard.GetBlockersAsync(_harness.Customer, default));
        Assert.Equal([BookingsDeletionGuard.PartnerBookingsInProgress], await Guard.GetBlockersAsync(_harness.Owner, default));
    }

    [Fact]
    public async Task A_confirmed_booking_still_blocks_but_a_cancelled_one_does_not()
    {
        var created = (await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default)).Value!;
        var accepted = (await _harness.Bookings.AcceptAsync(_harness.BusinessId, created.Id, created.Version, _harness.Owner, default)).Value!;
        Assert.NotEmpty(await Guard.GetBlockersAsync(_harness.Customer, default));

        await _harness.Bookings.CancelMineAsync(accepted.Id, _harness.Customer, null, accepted.Version, default);

        Assert.Empty(await Guard.GetBlockersAsync(_harness.Customer, default));
        Assert.Empty(await Guard.GetBlockersAsync(_harness.Owner, default));
    }

    [Fact]
    public async Task Someone_elses_booking_does_not_block_a_stranger()
    {
        await _harness.Bookings.CreateAsync(_harness.Customer, _harness.Input(), default);

        Assert.Empty(await Guard.GetBlockersAsync(Guid.NewGuid(), default));
    }
}
