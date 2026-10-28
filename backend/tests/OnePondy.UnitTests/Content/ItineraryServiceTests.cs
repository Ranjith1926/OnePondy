using OnePondy.Application.Itineraries;
using OnePondy.Domain.Itineraries;

namespace OnePondy.UnitTests.Content;

public class ItineraryServiceTests
{
    private readonly ContentHarness _harness = new();

    [Fact]
    public async Task Creating_an_itinerary_validates_the_title_and_dates()
    {
        var blankTitle = await _harness.Itineraries.CreateAsync(
            _harness.Customer,
            new ItineraryInput(" ", null, null),
            null,
            default);
        Assert.Equal(["title"], blankTitle.Error!.FieldErrors!.Keys);

        var badDates = await _harness.Itineraries.CreateAsync(
            _harness.Customer,
            new ItineraryInput("Trip", new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 1)),
            null,
            default);
        Assert.Equal(["endDate"], badDates.Error!.FieldErrors!.Keys);

        var ok = await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput(" Weekend trip ", null, null), null, default);
        Assert.Equal("Weekend trip", ok.Value!.Title);
        Assert.Equal(ItineraryStatus.Saved, ok.Value.Status);
    }

    [Fact]
    public async Task Only_the_owner_can_see_or_change_an_itinerary()
    {
        var mine = (await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput("Mine", null, null), null, default)).Value!;
        var otherCustomer = _harness.AddStaff("someone-else@onepondy.test", OnePondy.Domain.Identity.RoleNames.ContentEditor);

        Assert.Equal("itinerary_not_found", (await _harness.Itineraries.GetAsync(mine.Id, otherCustomer, null, default)).Error!.Code);
        Assert.Equal(
            "itinerary_not_found",
            (await _harness.Itineraries.UpdateAsync(mine.Id, otherCustomer, new ItineraryInput("Hijacked", null, null), null, default)).Error!.Code);
        Assert.NotNull((await _harness.Itineraries.GetAsync(mine.Id, _harness.Customer, null, default)).Value);
    }

    [Fact]
    public async Task Only_visible_places_can_be_added_as_stops()
    {
        var itinerary = (await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput("Trip", null, null), null, default)).Value!;
        var draft = (await _harness.Places.CreateAsync(ContentHarness.Input(name: "Secret spot"), _harness.Editor, default)).Value!;

        var error = await _harness.Itineraries.AddStopAsync(
            itinerary.Id,
            _harness.Customer,
            new AddStopInput(draft.Id, 1, null, null),
            null,
            default);

        Assert.Equal("place_not_available", error.Error!.Code);
    }

    [Fact]
    public async Task Adding_and_removing_stops_updates_the_detail_and_never_loses_other_stops()
    {
        var itinerary = (await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput("Trip", null, null), null, default)).Value!;
        var beach = await _harness.PublishedPlaceAsync();
        var lake = await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Ousteri Lake"));

        var afterFirst = (await _harness.Itineraries.AddStopAsync(itinerary.Id, _harness.Customer, new AddStopInput(beach.Id, 1, "Sunrise", null), null, default)).Value!;
        var afterSecond = (await _harness.Itineraries.AddStopAsync(itinerary.Id, _harness.Customer, new AddStopInput(lake.Id, 1, null, null), null, default)).Value!;

        Assert.Equal(2, afterSecond.Stops.Count);
        Assert.Equal("Sunrise", afterSecond.Stops[0].Notes);

        var afterRemove = (await _harness.Itineraries.RemoveStopAsync(itinerary.Id, _harness.Customer, afterFirst.Stops[0].Id, null, default)).Value!;
        Assert.Equal(lake.Id, Assert.Single(afterRemove.Stops).Place.Id);
    }

    [Fact]
    public async Task An_itinerary_can_hold_at_most_the_configured_number_of_stops()
    {
        var itinerary = (await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput("Big trip", null, null), null, default)).Value!;
        var place = await _harness.PublishedPlaceAsync();

        for (var day = 1; day <= Itinerary.MaxStops; day++)
        {
            var result = await _harness.Itineraries.AddStopAsync(itinerary.Id, _harness.Customer, new AddStopInput(place.Id, day, null, null), null, default);
            Assert.True(result.IsSuccess);
        }

        var overflow = await _harness.Itineraries.AddStopAsync(itinerary.Id, _harness.Customer, new AddStopInput(place.Id, 1, null, null), null, default);
        Assert.Equal("too_many_stops", overflow.Error!.Code);
    }

    [Fact]
    public async Task Reordering_moves_stops_between_days_in_one_call()
    {
        var itinerary = (await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput("Trip", null, null), null, default)).Value!;
        var beach = await _harness.PublishedPlaceAsync();
        var lake = await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Ousteri Lake"));
        var withStops = (await _harness.Itineraries.AddStopAsync(itinerary.Id, _harness.Customer, new AddStopInput(beach.Id, 1, null, null), null, default)).Value!;
        withStops = (await _harness.Itineraries.AddStopAsync(itinerary.Id, _harness.Customer, new AddStopInput(lake.Id, 1, null, null), null, default)).Value!;

        var reordered = (await _harness.Itineraries.ReorderStopsAsync(
            itinerary.Id,
            _harness.Customer,
            [new StopPositionInput(withStops.Stops[0].Id, 2, 0), new StopPositionInput(withStops.Stops[1].Id, 1, 0)],
            null,
            default)).Value!;

        Assert.Equal(1, reordered.Stops.Single(s => s.Place.Id == lake.Id).DayNumber);
        Assert.Equal(2, reordered.Stops.Single(s => s.Place.Id == beach.Id).DayNumber);
    }

    [Fact]
    public async Task A_shared_link_shows_the_plan_but_never_the_owner()
    {
        var itinerary = (await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput("Honeymoon trip", null, null), null, default)).Value!;
        var place = await _harness.PublishedPlaceAsync();
        await _harness.Itineraries.AddStopAsync(itinerary.Id, _harness.Customer, new AddStopInput(place.Id, 1, "Golden hour", null), null, default);

        Assert.Equal("itinerary_not_shared", (await _harness.Itineraries.GetSharedAsync("made-up-token", null, default)).Error!.Code);

        var token = (await _harness.Itineraries.EnableSharingAsync(itinerary.Id, _harness.Customer, default)).Value!;
        var shared = (await _harness.Itineraries.GetSharedAsync(token, null, default)).Value!;

        // SharedItineraryView has no user/owner field at all (see ItineraryViews.cs); this just
        // checks the plan itself comes through correctly.
        Assert.Equal("Honeymoon trip", shared.Title);
        Assert.Equal("Golden hour", Assert.Single(shared.Stops).Notes);

        await _harness.Itineraries.DisableSharingAsync(itinerary.Id, _harness.Customer, default);
        Assert.Equal("itinerary_not_shared", (await _harness.Itineraries.GetSharedAsync(token, null, default)).Error!.Code);
    }

    [Fact]
    public async Task Listing_hides_archived_itineraries_unless_asked_for()
    {
        var kept = (await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput("Kept", null, null), null, default)).Value!;
        var archived = (await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput("Old trip", null, null), null, default)).Value!;
        await _harness.Itineraries.SetArchivedAsync(archived.Id, _harness.Customer, archived: true, null, default);

        var active = await _harness.Itineraries.ListAsync(_harness.Customer, includeArchived: false, default);
        var everything = await _harness.Itineraries.ListAsync(_harness.Customer, includeArchived: true, default);

        Assert.Equal(kept.Id, Assert.Single(active).Id);
        Assert.Equal(2, everything.Count);
    }

    [Fact]
    public async Task Deleting_an_itinerary_removes_it_and_its_stops()
    {
        var itinerary = (await _harness.Itineraries.CreateAsync(_harness.Customer, new ItineraryInput("Gone soon", null, null), null, default)).Value!;
        var place = await _harness.PublishedPlaceAsync();
        await _harness.Itineraries.AddStopAsync(itinerary.Id, _harness.Customer, new AddStopInput(place.Id, 1, null, null), null, default);

        Assert.Null(await _harness.Itineraries.DeleteAsync(itinerary.Id, _harness.Customer, default));
        Assert.Equal("itinerary_not_found", (await _harness.Itineraries.GetAsync(itinerary.Id, _harness.Customer, null, default)).Error!.Code);
        Assert.Empty(await _harness.Itineraries.ListAsync(_harness.Customer, includeArchived: true, default));
    }
}
