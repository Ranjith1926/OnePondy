using OnePondy.Domain.Listings;

namespace OnePondy.UnitTests.Domain;

public class ListingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid BusinessId = Guid.NewGuid();

    private static ListingContent Content(
        ListingType type = ListingType.Activity,
        bool isBookable = true,
        CancellationPolicy? policy = null) =>
        new(type, "Backwater Kayaking", "A two-hour guided kayak tour.", isBookable, 500m, isBookable ? policy ?? new CancellationPolicy(24, 50, null) : null);

    [Fact]
    public void A_new_listing_starts_as_a_draft()
    {
        var listing = Listing.Create(BusinessId, Content(), Now);

        Assert.Equal(ListingStatus.Draft, listing.Status);
        Assert.True(listing.CanEdit);
    }

    [Fact]
    public void A_shop_listing_cannot_be_bookable()
    {
        Assert.Throws<ArgumentException>(() => Listing.Create(BusinessId, Content(ListingType.Shop, isBookable: true), Now));
    }

    [Fact]
    public void A_bookable_listing_needs_a_cancellation_policy()
    {
        var content = Content() with { CancellationPolicy = null };
        Assert.Throws<ArgumentException>(() => Listing.Create(BusinessId, content, Now));
    }

    [Fact]
    public void Only_a_draft_listing_can_be_edited_or_submitted()
    {
        var listing = Listing.Create(BusinessId, Content(), Now);
        listing.Submit(Now);

        Assert.False(listing.CanEdit);
        Assert.Throws<InvalidOperationException>(() => listing.UpdateContent(Content(), Now));
        Assert.Throws<InvalidOperationException>(() => listing.Submit(Now));
    }

    [Fact]
    public void Publishing_requires_a_pending_listing_and_clears_any_review_note()
    {
        var listing = Listing.Create(BusinessId, Content(), Now);
        Assert.Throws<InvalidOperationException>(() => listing.Publish(Now));

        listing.Submit(Now);
        listing.SendBackToDraft("Add opening hours.", Now);
        Assert.Equal(ListingStatus.Draft, listing.Status);
        Assert.Equal("Add opening hours.", listing.ReviewNote);

        listing.Submit(Now);
        listing.Publish(Now);
        Assert.Equal(ListingStatus.Published, listing.Status);
        Assert.Null(listing.ReviewNote);
    }

    [Fact]
    public void A_published_listing_can_be_suspended_and_reinstated()
    {
        var listing = Listing.Create(BusinessId, Content(), Now);
        listing.Submit(Now);
        listing.Publish(Now);

        listing.Suspend("Customer complaints about safety.", Now);
        Assert.Equal(ListingStatus.Suspended, listing.Status);
        Assert.Throws<InvalidOperationException>(() => listing.Suspend("Again", Now));

        listing.Reinstate(Now);
        Assert.Equal(ListingStatus.Published, listing.Status);
        Assert.Null(listing.ReviewNote);
    }

    [Fact]
    public void Editing_a_draft_resubmitted_from_a_sent_back_listing_clears_the_review_note()
    {
        var listing = Listing.Create(BusinessId, Content(), Now);
        listing.Submit(Now);
        listing.SendBackToDraft("Fix the description.", Now);

        listing.UpdateContent(Content(), Now);

        Assert.Null(listing.ReviewNote);
    }
}

public class AvailabilitySlotDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid ListingId = Guid.NewGuid();

    [Fact]
    public void A_slot_must_end_after_it_starts_and_have_at_least_one_place()
    {
        Assert.Throws<ArgumentException>(() => AvailabilitySlot.Create(ListingId, Now, Now, 5, Now));
        Assert.Throws<ArgumentException>(() => AvailabilitySlot.Create(ListingId, Now, Now.AddHours(2), 0, Now));
    }

    [Fact]
    public void Reserving_more_than_the_remaining_capacity_fails()
    {
        var slot = AvailabilitySlot.Create(ListingId, Now, Now.AddHours(2), 5, Now);

        slot.Reserve(3, Now);
        Assert.Equal(2, slot.AvailableCount);

        Assert.Throws<InvalidOperationException>(() => slot.Reserve(3, Now));
    }

    [Fact]
    public void Releasing_never_drops_the_reserved_count_below_zero()
    {
        var slot = AvailabilitySlot.Create(ListingId, Now, Now.AddHours(2), 5, Now);
        slot.Reserve(2, Now);

        slot.Release(5, Now);

        Assert.Equal(0, slot.ReservedCount);
    }

    [Fact]
    public void Capacity_cannot_be_reduced_below_what_is_already_reserved()
    {
        var slot = AvailabilitySlot.Create(ListingId, Now, Now.AddHours(2), 5, Now);
        slot.Reserve(4, Now);

        Assert.Throws<InvalidOperationException>(() => slot.UpdateCapacity(3, Now));
    }
}
