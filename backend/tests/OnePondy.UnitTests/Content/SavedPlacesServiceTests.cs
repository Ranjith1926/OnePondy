namespace OnePondy.UnitTests.Content;

public class SavedPlacesServiceTests
{
    private readonly ContentHarness _harness = new();

    [Fact]
    public async Task A_customer_can_save_and_list_a_published_place()
    {
        var place = await _harness.PublishedPlaceAsync();

        Assert.Null(await _harness.SavedPlaces.SaveAsync(_harness.Customer, place.Id, default));

        var saved = await _harness.SavedPlaces.ListAsync(_harness.Customer, null, default);
        Assert.Equal(place.Id, Assert.Single(saved).Place.Id);
    }

    [Fact]
    public async Task Only_places_a_guest_could_already_see_can_be_saved()
    {
        var draft = (await _harness.Places.CreateAsync(ContentHarness.Input(), _harness.Editor, default)).Value!;

        var error = await _harness.SavedPlaces.SaveAsync(_harness.Customer, draft.Id, default);

        Assert.Equal("place_not_available", error!.Code);
        Assert.Empty(await _harness.SavedPlaces.ListAsync(_harness.Customer, null, default));
    }

    [Fact]
    public async Task Saving_twice_or_removing_what_was_never_saved_changes_nothing()
    {
        var place = await _harness.PublishedPlaceAsync();

        await _harness.SavedPlaces.SaveAsync(_harness.Customer, place.Id, default);
        Assert.Null(await _harness.SavedPlaces.SaveAsync(_harness.Customer, place.Id, default));
        Assert.Single(await _harness.SavedPlaces.ListAsync(_harness.Customer, null, default));

        await _harness.SavedPlaces.RemoveAsync(_harness.Customer, place.Id, default);
        Assert.Empty(await _harness.SavedPlaces.ListAsync(_harness.Customer, null, default));

        // Removing again is harmless.
        await _harness.SavedPlaces.RemoveAsync(_harness.Customer, place.Id, default);
    }

    [Fact]
    public async Task A_saved_place_that_is_later_unpublished_still_shows_but_is_flagged()
    {
        var place = await _harness.PublishedPlaceAsync();
        await _harness.SavedPlaces.SaveAsync(_harness.Customer, place.Id, default);

        await _harness.Places.UnpublishAsync(place.Id, place.Version, _harness.Editor, default);

        var saved = Assert.Single(await _harness.SavedPlaces.ListAsync(_harness.Customer, null, default));
        Assert.False(saved.Place.IsCurrentlyPublished);
    }
}
