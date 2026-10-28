using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Application.TripPlanner;
using OnePondy.Domain.Itineraries;

namespace OnePondy.UnitTests.Content;

public class TripPlannerServiceTests
{
    private readonly ContentHarness _harness = new();

    private static TripPlanRequest Request(
        IReadOnlyList<string>? interests = null,
        DateOnly? start = null,
        DateOnly? end = null) =>
        new(
            start ?? new DateOnly(2026, 12, 1),
            end ?? new DateOnly(2026, 12, 2),
            interests ?? ["beach"],
            TripPace.Balanced,
            TripBudgetBand.Medium,
            MobilityNeeds: "Needs step-free access",
            Latitude: null,
            Longitude: null);

    [Fact]
    public async Task The_planner_is_disabled_until_the_module_flag_turns_it_on()
    {
        var result = await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(), null, default);

        Assert.Equal("trip_planner_disabled", result.Error!.Code);
        Assert.Equal(0, _harness.Planner.CallCount);
    }

    [Fact]
    public async Task Candidates_sent_to_the_provider_are_only_published_verified_places_matching_interests()
    {
        await _harness.EnableTripPlannerAsync();
        var beach = await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Paradise Beach"));
        var draftMuseum = (await _harness.Places.CreateAsync(
            ContentHarness.Input(name: "Draft Museum") with { CategoryCode = "museum" },
            _harness.Editor,
            default)).Value!;
        _harness.Planner.NextResult = Result<IReadOnlyList<PlannerSuggestion>>.Success([]);

        await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(["beach", "museum"]), null, default);

        var candidateIds = _harness.Planner.LastCandidates!.Select(c => c.PlaceId).ToList();
        Assert.Contains(beach.Id, candidateIds);
        Assert.DoesNotContain(draftMuseum.Id, candidateIds);
    }

    [Fact]
    public async Task No_matching_candidates_returns_an_error_without_calling_the_provider()
    {
        await _harness.EnableTripPlannerAsync();

        var result = await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(["heritage"]), null, default);

        Assert.Equal("trip_planner_no_candidates", result.Error!.Code);
        Assert.Equal(0, _harness.Planner.CallCount);
    }

    [Fact]
    public async Task Suggestions_outside_the_candidate_set_or_the_trips_own_days_are_discarded()
    {
        await _harness.EnableTripPlannerAsync();
        var beach = await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Paradise Beach"));
        _harness.Planner.NextResult = Result<IReadOnlyList<PlannerSuggestion>>.Success(
        [
            new PlannerSuggestion(beach.Id, DayNumber: 1, StopOrder: 0),
            new PlannerSuggestion(Guid.NewGuid(), DayNumber: 1, StopOrder: 1), // not a candidate
            new PlannerSuggestion(beach.Id, DayNumber: 99, StopOrder: 0), // outside the trip
        ]);

        var result = await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(), null, default);

        var stop = Assert.Single(result.Value!.Stops);
        Assert.Equal(beach.Id, stop.Place.Id);
    }

    [Fact]
    public async Task A_successful_plan_creates_an_ai_assisted_draft_the_user_must_still_save()
    {
        await _harness.EnableTripPlannerAsync();
        var beach = await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Paradise Beach"));
        _harness.Planner.NextResult = Result<IReadOnlyList<PlannerSuggestion>>.Success(
            [new PlannerSuggestion(beach.Id, DayNumber: 1, StopOrder: 0)]);

        var result = await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(), null, default);

        var draft = result.Value!;
        Assert.Equal(ItineraryStatus.Draft, draft.Status);
        Assert.Equal(ItinerarySource.AiAssisted, draft.Source);

        var saved = await _harness.Itineraries.ConfirmAsync(draft.Id, _harness.Customer, null, default);
        Assert.Equal(ItineraryStatus.Saved, saved.Value!.Status);
    }

    [Fact]
    public async Task When_the_provider_fails_no_draft_is_created()
    {
        await _harness.EnableTripPlannerAsync();
        await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Paradise Beach"));
        _harness.Planner.NextResult = Result<IReadOnlyList<PlannerSuggestion>>.Failure(TripPlannerErrors.ProviderUnavailable);

        var result = await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(), null, default);

        Assert.Equal("trip_planner_provider_unavailable", result.Error!.Code);
        Assert.Empty(await _harness.Itineraries.ListAsync(_harness.Customer, includeArchived: true, default));
    }

    [Fact]
    public async Task A_user_cannot_make_more_than_the_daily_request_limit()
    {
        await _harness.EnableTripPlannerAsync();
        await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Paradise Beach"));
        _harness.Planner.NextResult = Result<IReadOnlyList<PlannerSuggestion>>.Success([]);

        // The harness's limit is 2 a day; "no candidates" requests still count as attempts.
        await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(["heritage"]), null, default);
        await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(["heritage"]), null, default);
        var result = await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(["heritage"]), null, default);

        Assert.Equal("trip_planner_rate_limited", result.Error!.Code);
        Assert.NotNull(result.Error.RetryAfter);
    }

    [Fact]
    public async Task The_daily_cost_cap_stops_new_requests_once_it_would_be_exceeded()
    {
        await _harness.EnableTripPlannerAsync();
        var beach = await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Paradise Beach"));
        _harness.Planner.NextResult = Result<IReadOnlyList<PlannerSuggestion>>.Success(
            [new PlannerSuggestion(beach.Id, DayNumber: 1, StopOrder: 0)]);

        // The harness caps spend at $0.05 and estimates $0.02 per call, so a third call (for a
        // different user, to stay under the per-user limit) would push spend past the cap.
        await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(), null, default);
        var otherUser = Guid.NewGuid();
        await _harness.TripPlanner.PlanAsync(otherUser, Request(), null, default);
        var result = await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(), null, default);

        Assert.Equal("trip_planner_daily_cap_reached", result.Error!.Code);
    }

    [Fact]
    public async Task The_request_log_keeps_trip_details_but_never_the_mobility_notes_the_user_typed()
    {
        await _harness.EnableTripPlannerAsync();
        var beach = await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Paradise Beach"));
        _harness.Planner.NextResult = Result<IReadOnlyList<PlannerSuggestion>>.Success(
            [new PlannerSuggestion(beach.Id, DayNumber: 1, StopOrder: 0)]);

        await _harness.TripPlanner.PlanAsync(_harness.Customer, Request(start: new DateOnly(2026, 12, 1), end: new DateOnly(2026, 12, 3)), null, default);

        var log = await _harness.Db.TripPlannerRequestLogs.SingleAsync();
        Assert.Equal(3, log.DayCount);
        Assert.Equal("beach", log.Interests);
        Assert.Equal(1, log.SuggestedCount);
    }
}
