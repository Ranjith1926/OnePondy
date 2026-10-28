using OnePondy.Domain.Listings;

namespace OnePondy.UnitTests.Listings;

public class ListingServiceTests
{
    private readonly ListingHarness _harness = new();

    [Fact]
    public async Task A_listing_cannot_be_created_under_a_business_that_is_not_yet_approved()
    {
        var pendingOwner = Guid.NewGuid();
        var result = await _harness.Listings.CreateAsync(Guid.NewGuid(), pendingOwner, ListingHarness.Input(), default);

        Assert.Equal("listing_not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Submitting_and_publishing_makes_a_listing_visible_to_the_public()
    {
        var published = await _harness.PublishedListingAsync();

        Assert.Equal(ListingStatus.Published, published.Status);

        var publicResult = await _harness.Listings.GetPublicAsync(published.Id, default);
        Assert.True(publicResult.IsSuccess);
        Assert.Equal(published.Title, publicResult.Value!.Title);
    }

    [Fact]
    public async Task A_draft_listing_is_not_visible_to_the_public()
    {
        var created = await _harness.Listings.CreateAsync(_harness.BusinessId, _harness.Owner, ListingHarness.Input(), default);

        var publicResult = await _harness.Listings.GetPublicAsync(created.Value!.Id, default);

        Assert.Equal("listing_not_found", publicResult.Error!.Code);
    }

    [Fact]
    public async Task Sending_a_pending_listing_back_lets_the_owner_edit_and_resubmit_it()
    {
        var created = await _harness.Listings.CreateAsync(_harness.BusinessId, _harness.Owner, ListingHarness.Input(), default);
        var submitted = await _harness.Listings.SubmitAsync(created.Value!.Id, created.Value.Version, _harness.Owner, default);

        var sentBack = await _harness.Listings.SendBackAsync(submitted.Value!.Id, submitted.Value.Version, "Add more photos.", _harness.Administrator, default);
        Assert.Equal(ListingStatus.Draft, sentBack.Value!.Status);
        Assert.Equal("Add more photos.", sentBack.Value.ReviewNote);

        var updated = await _harness.Listings.UpdateAsync(sentBack.Value.Id, sentBack.Value.Version, _harness.Owner, ListingHarness.Input(), default);
        Assert.Null(updated.Value!.ReviewNote);
    }

    [Fact]
    public async Task A_shop_listing_cannot_be_bookable()
    {
        var result = await _harness.Listings.CreateAsync(
            _harness.BusinessId,
            _harness.Owner,
            ListingHarness.Input(ListingType.Shop, isBookable: true),
            default);

        Assert.Equal("validation_failed", result.Error!.Code);
    }

    [Fact]
    public async Task Availability_slots_can_be_created_updated_and_removed()
    {
        var listing = await _harness.PublishedListingAsync();
        var start = _harness.Time.GetUtcNow().AddDays(1);

        var created = await _harness.Listings.CreateSlotAsync(listing.Id, _harness.Owner, start, start.AddHours(2), 8, default);
        Assert.True(created.IsSuccess);
        Assert.Equal(8, created.Value!.AvailableCount);

        var updated = await _harness.Listings.UpdateSlotCapacityAsync(listing.Id, created.Value.Id, created.Value.Version, _harness.Owner, 10, default);
        Assert.Equal(10, updated.Value!.Capacity);

        var removed = await _harness.Listings.DeleteSlotAsync(listing.Id, created.Value.Id, _harness.Owner, default);
        Assert.Null(removed);
    }

    [Fact]
    public async Task A_slot_with_reservations_cannot_be_removed()
    {
        var listing = await _harness.PublishedListingAsync();
        var start = _harness.Time.GetUtcNow().AddDays(1);
        var slot = (await _harness.Listings.CreateSlotAsync(listing.Id, _harness.Owner, start, start.AddHours(2), 8, default)).Value!;

        var tracked = await _harness.Db.AvailabilitySlots.FindAsync([slot.Id], CancellationToken.None);
        tracked!.Reserve(2, _harness.Time.GetUtcNow());
        await _harness.Db.SaveChangesAsync(default);

        var removed = await _harness.Listings.DeleteSlotAsync(listing.Id, slot.Id, _harness.Owner, default);

        Assert.Equal("slot_has_reservations", removed!.Code);
    }
}
