using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Rides;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Rides;

namespace OnePondy.UnitTests.Rides;

/// <summary>The arithmetic of a fare, and the settings that guard it.</summary>
public class RideFareTests
{
    [Fact]
    public void Distance_is_the_same_both_ways_and_close_to_the_known_road_between_two_places()
    {
        var there = RideFare.StraightLineKm(RideHarness.PromenadeLatitude, RideHarness.PromenadeLongitude, RideHarness.AurovilleLatitude, RideHarness.AurovilleLongitude);
        var back = RideFare.StraightLineKm(RideHarness.AurovilleLatitude, RideHarness.AurovilleLongitude, RideHarness.PromenadeLatitude, RideHarness.PromenadeLongitude);

        Assert.Equal(there, back, 6);
        Assert.InRange(there, 8.0, 9.0);
        Assert.Equal(0, RideFare.StraightLineKm(11.9, 79.8, 11.9, 79.8), 9);
    }

    [Fact]
    public void Road_distance_is_the_straight_line_times_the_factor_to_one_decimal_and_never_below_a_tenth()
    {
        Assert.Equal(13.0m, RideFare.RoadKm(10.0, 1.3m));
        Assert.Equal(1.4m, RideFare.RoadKm(1.04, 1.35m));
        Assert.Equal(0.1m, RideFare.RoadKm(0.0, 1.3m));
    }

    [Fact]
    public void Fare_is_base_plus_the_per_km_rate_rounded_up_and_never_below_the_minimum()
    {
        var auto = new RideTariff(30m, 15m, 40m);

        Assert.Equal(40m, RideFare.Fare(auto, 0.5m));
        Assert.Equal(225m, RideFare.Fare(auto, 13.0m));
        Assert.Equal(226m, RideFare.Fare(auto, 13.05m));
    }

    [Fact]
    public void Settings_that_would_give_nonsense_fares_stop_the_API_starting()
    {
        RideOptionsGuard.EnsureSafe(new RideOptions());
        Assert.Throws<InvalidOperationException>(() => RideOptionsGuard.EnsureSafe(new RideOptions { RoadFactor = 0.5m }));
        Assert.Throws<InvalidOperationException>(() => RideOptionsGuard.EnsureSafe(new RideOptions { QuoteValidFor = TimeSpan.Zero }));
        Assert.Throws<InvalidOperationException>(() => RideOptionsGuard.EnsureSafe(new RideOptions { OfferRadiusKm = 0 }));
        Assert.Throws<InvalidOperationException>(() => RideOptionsGuard.EnsureSafe(new RideOptions { CancellationTerms = " " }));
        Assert.Throws<InvalidOperationException>(() => RideOptionsGuard.EnsureSafe(new RideOptions { Tariffs = new() { [RideVehicleClass.Auto] = new RideTariff(30m, 0m, 40m) } }));
    }
}

/// <summary>Phase 9 step 9.2: fare quotes, requests, dispatch, and the trip from request to the end.</summary>
public class RideTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly RideHarness _h = new();

    // --- Quotes ---

    [Fact]
    public async Task Rides_are_unavailable_until_the_module_is_switched_on()
    {
        var off = new RideHarness(ridesOn: false);

        var quote = await off.Rides.QuoteAsync(off.Rider, off.Trip(), None);
        var request = await off.Rides.RequestAsync(off.Rider, new RequestRideInput(Guid.NewGuid(), null), None);

        Assert.Equal(RideErrors.ModuleOff, quote.Error);
        Assert.Equal(RideErrors.ModuleOff, request.Error);
    }

    [Fact]
    public async Task A_quote_offers_each_enabled_class_with_the_servers_fare_and_says_which_have_a_driver_near()
    {
        await _h.OnDutyDriverAsync("+919000011111", RideVehicleClass.Auto);

        var quotes = await _h.Rides.QuoteAsync(_h.Rider, _h.Trip(), None);

        Assert.True(quotes.IsSuccess, quotes.Error?.Detail);
        Assert.Equal([RideVehicleClass.Auto, RideVehicleClass.Cab], quotes.Value!.Options.Select(o => o.VehicleClass));
        var auto = quotes.Value.Options[0];
        var cab = quotes.Value.Options[1];
        Assert.True(auto.Available);
        Assert.False(cab.Available);
        Assert.InRange(auto.DistanceKm, 10m, 13m);
        Assert.Equal(RideFare.Fare(_h.Options.Tariffs[RideVehicleClass.Auto], auto.DistanceKm), auto.Fare);
        Assert.True(cab.Fare > auto.Fare);
        Assert.Equal(_h.Options.CancellationTerms, auto.CancellationTerms);
        Assert.Equal(_h.B.Time.GetUtcNow() + _h.Options.QuoteValidFor, auto.ExpiresAt);
        Assert.Equal(2, await _h.B.Db.RideQuotes.CountAsync());
    }

    [Fact]
    public async Task Bike_taxis_are_offered_only_when_the_owner_enables_them()
    {
        var quotes = await _h.Rides.QuoteAsync(_h.Rider, _h.Trip(), None);
        Assert.DoesNotContain(quotes.Value!.Options, o => o.VehicleClass == RideVehicleClass.BikeTaxi);

        _h.Options.EnabledClasses.Add(RideVehicleClass.BikeTaxi);
        var more = await _h.Rides.QuoteAsync(_h.Rider, _h.Trip(), None);
        Assert.Contains(more.Value!.Options, o => o.VehicleClass == RideVehicleClass.BikeTaxi);
    }

    [Fact]
    public async Task Places_must_be_real_labelled_inside_the_service_area_and_a_sensible_distance_apart()
    {
        var outside = await _h.Rides.QuoteAsync(_h.Rider, new RideQuoteInput(13.0827, 80.2707, "Chennai", RideHarness.AurovilleLatitude, RideHarness.AurovilleLongitude, "Auroville"), None);
        var nowhere = await _h.Rides.QuoteAsync(_h.Rider, new RideQuoteInput(null, null, " ", 95, 79.8, null), None);
        var tooClose = await _h.Rides.QuoteAsync(_h.Rider, new RideQuoteInput(11.9340, 79.8350, "Here", 11.9341, 79.8351, "Also here"), None);
        _h.Options.MaxTripKm = 5;
        var tooFar = await _h.Rides.QuoteAsync(_h.Rider, _h.Trip(), None);

        Assert.Contains("pickup", outside.Error!.FieldErrors!.Keys);
        Assert.Contains("pickupLabel", nowhere.Error!.FieldErrors!.Keys);
        Assert.Contains("destination", nowhere.Error.FieldErrors.Keys);
        Assert.Contains("destination", tooClose.Error!.FieldErrors!.Keys);
        Assert.Contains("destination", tooFar.Error!.FieldErrors!.Keys);
        Assert.False(await _h.B.Db.RideQuotes.AnyAsync());
    }

    [Fact]
    public async Task Only_an_eligible_driver_on_duty_with_a_fresh_position_close_by_makes_a_class_available()
    {
        var offDuty = await _h.OnDutyDriverAsync("+919000011111", plate: "PY 01 AA 0001", licence: "PY-0120200000001");
        var version = (await _h.Drivers.GetMineAsync(offDuty, None)).Version;
        await _h.Drivers.SetDutyAsync(offDuty, false, version, None);
        await _h.OnDutyDriverAsync("+919000022222", plate: "PY 01 AA 0002", licence: "PY-0120200000002", latitude: 12.20, longitude: 79.80);

        var none = (await _h.QuotedAsync()).Available;

        // A driver who is on duty, close by, but whose position is two minutes old has dropped off the map.
        await _h.OnDutyDriverAsync("+919000033333", plate: "PY 01 AA 0003", licence: "PY-0120200000003");
        Assert.True((await _h.QuotedAsync()).Available);
        _h.B.Time.Advance(RideDriverProfile.PositionFreshFor + TimeSpan.FromSeconds(1));
        var stale = (await _h.QuotedAsync()).Available;

        Assert.False(none);
        Assert.False(stale);
    }

    // --- Requests ---

    [Fact]
    public async Task A_request_carries_the_quoted_fare_even_if_the_tariff_changes_afterwards()
    {
        await _h.OnDutyDriverAsync("+919000011111");
        var quote = await _h.QuotedAsync();
        _h.Options.Tariffs[RideVehicleClass.Auto] = new RideTariff(500m, 100m, 500m);

        var ride = await _h.Rides.RequestAsync(_h.Rider, new RequestRideInput(quote.QuoteId, "  Blue gate  "), None);

        Assert.True(ride.IsSuccess, ride.Error?.Detail);
        Assert.Equal(quote.Fare, ride.Value!.Fare);
        Assert.Equal(RideStatus.Requested, ride.Value.Status);
        Assert.Equal("Blue gate", ride.Value.PickupNote);
        Assert.Equal(_h.Options.CancellationTerms, ride.Value.CancellationTerms);
        Assert.Equal(_h.B.Time.GetUtcNow() + _h.Options.SearchWindow, ride.Value.SearchEndsAt);
    }

    [Fact]
    public async Task A_request_needs_a_real_unexpired_unused_quote_of_the_riders_own()
    {
        await _h.OnDutyDriverAsync("+919000011111");
        var quote = await _h.QuotedAsync();
        var stranger = await _h.NewAccountAsync("+919000099999");

        var missing = await _h.Rides.RequestAsync(_h.Rider, new RequestRideInput(null, null), None);
        var unknown = await _h.Rides.RequestAsync(_h.Rider, new RequestRideInput(Guid.NewGuid(), null), None);
        var notYours = await _h.Rides.RequestAsync(stranger, new RequestRideInput(quote.QuoteId, null), None);
        _h.B.Time.Advance(_h.Options.QuoteValidFor + TimeSpan.FromSeconds(1));
        var expired = await _h.Rides.RequestAsync(_h.Rider, new RequestRideInput(quote.QuoteId, null), None);

        Assert.Contains("quoteId", missing.Error!.FieldErrors!.Keys);
        Assert.Equal(RideErrors.QuoteNotFound, unknown.Error);
        Assert.Equal(RideErrors.QuoteNotFound, notYours.Error);
        Assert.Equal(RideErrors.QuoteExpired, expired.Error);
    }

    [Fact]
    public async Task A_quote_makes_one_ride_and_a_rider_has_one_open_ride_at_a_time()
    {
        await _h.OnDutyDriverAsync("+919000011111");
        var quote = await _h.QuotedAsync();
        var other = await _h.QuotedAsync();
        var first = await _h.Rides.RequestAsync(_h.Rider, new RequestRideInput(quote.QuoteId, null), None);

        var again = await _h.Rides.RequestAsync(_h.Rider, new RequestRideInput(quote.QuoteId, null), None);
        var second = await _h.Rides.RequestAsync(_h.Rider, new RequestRideInput(other.QuoteId, null), None);

        Assert.True(first.IsSuccess);
        Assert.Equal(RideErrors.QuoteUsed, again.Error);
        Assert.Equal(RideErrors.RiderBusy, second.Error);
        Assert.Equal(1, await _h.B.Db.Rides.CountAsync());
    }

    [Fact]
    public async Task A_request_is_refused_when_no_eligible_driver_is_near()
    {
        await _h.OnDutyDriverAsync("+919000011111");
        var quote = await _h.QuotedAsync();
        _h.B.Time.Advance(RideDriverProfile.PositionFreshFor + TimeSpan.FromSeconds(1));
        var fresh = await _h.QuotedAsync();

        var result = await _h.Rides.RequestAsync(_h.Rider, new RequestRideInput(fresh.QuoteId, null), None);

        Assert.False(quote.QuoteId == Guid.Empty);
        Assert.Equal(RideErrors.NoDrivers, result.Error);
        Assert.False(await _h.B.Db.Rides.AnyAsync());
    }

    [Fact]
    public async Task A_class_that_is_no_longer_offered_cannot_be_requested()
    {
        await _h.OnDutyDriverAsync("+919000011111");
        var quote = await _h.QuotedAsync();
        _h.Options.EnabledClasses.Remove(RideVehicleClass.Auto);

        var result = await _h.Rides.RequestAsync(_h.Rider, new RequestRideInput(quote.QuoteId, null), None);

        Assert.Equal(RideErrors.ClassUnavailable, result.Error);
    }

    // --- Offers ---

    [Fact]
    public async Task A_driver_is_offered_only_requests_of_their_class_near_their_position_and_sees_little_of_them()
    {
        var auto = await _h.OnDutyDriverAsync("+919000011111", RideVehicleClass.Auto, "PY 01 AA 0001", "PY-0120200000001");
        var cab = await _h.OnDutyDriverAsync("+919000022222", RideVehicleClass.Cab, "PY 01 AA 0002", "PY-0120200000002");
        var farAuto = await _h.OnDutyDriverAsync("+919000033333", RideVehicleClass.Auto, "PY 01 AA 0003", "PY-0120200000003", 12.10, 79.80);
        await _h.RequestedAsync(RideVehicleClass.Auto, note: "Behind the blue gate");

        var autoOffers = await _h.Rides.GetMineAsync(auto, None);
        var cabOffers = await _h.Rides.GetMineAsync(cab, None);
        var farOffers = await _h.Rides.GetMineAsync(farAuto, None);

        var offer = Assert.Single(autoOffers.Offers);
        Assert.Equal("Promenade Beach", offer.PickupLabel);
        Assert.Equal("Auroville", offer.DestinationLabel);
        Assert.Equal(0, offer.PickupDistanceKm);
        Assert.Empty(cabOffers.Offers);
        Assert.Empty(farOffers.Offers);
        Assert.Null(autoOffers.Current);

        // What the offer is made of: no coordinates, no rider name, no note.
        var members = typeof(RideOfferView).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(members, m => m.Contains("Latitude") || m.Contains("Longitude") || m.Contains("Rider") || m.Contains("Note") || m.Contains("Phone"));
    }

    [Fact]
    public async Task A_driver_off_duty_uninsured_or_without_a_fresh_position_is_offered_nothing()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        await _h.RequestedAsync();
        _h.B.Time.Advance(RideDriverProfile.PositionFreshFor + TimeSpan.FromSeconds(1));

        var stale = await _h.Rides.GetMineAsync(driver, None);
        await _h.Drivers.ShareLocationAsync(driver, RideHarness.PromenadeLatitude, RideHarness.PromenadeLongitude, None);
        var fresh = await _h.Rides.GetMineAsync(driver, None);
        var version = (await _h.Drivers.GetMineAsync(driver, None)).Version;
        await _h.Drivers.SetDutyAsync(driver, false, version, None);
        var off = await _h.Rides.GetMineAsync(driver, None);

        Assert.Empty(stale.Offers);
        Assert.Single(fresh.Offers);
        Assert.Empty(off.Offers);
        Assert.False(off.Available);
    }

    [Fact]
    public async Task A_driver_who_never_applied_has_nothing()
    {
        var nobody = await _h.NewAccountAsync("+919000077777");

        var view = await _h.Rides.GetMineAsync(nobody, None);

        Assert.False(view.Available);
        Assert.Empty(view.Offers);
        Assert.Null(view.Current);
    }

    // --- Accepting ---

    [Fact]
    public async Task The_first_driver_to_accept_gets_the_ride_and_the_second_is_told_it_was_taken()
    {
        var first = await _h.OnDutyDriverAsync("+919000011111", plate: "PY 01 AA 0001", licence: "PY-0120200000001");
        var second = await _h.OnDutyDriverAsync("+919000022222", plate: "PY 01 AA 0002", licence: "PY-0120200000002");
        var ride = await _h.RequestedAsync(note: "Blue gate");

        var won = await _h.Rides.AcceptAsync(first, ride.Id, None);
        var lost = await _h.Rides.AcceptAsync(second, ride.Id, None);

        Assert.True(won.IsSuccess, won.Error?.Detail);
        Assert.Equal(RideStatus.Accepted, won.Value!.Status);
        Assert.Equal("Anita", won.Value.RiderFirstName);
        Assert.Equal("Blue gate", won.Value.PickupNote);
        Assert.Equal(RideHarness.PromenadeLatitude, won.Value.Pickup.Latitude);
        Assert.Equal(RideErrors.Taken, lost.Error);
        Assert.Equal(first, (await _h.RideRowAsync(ride.Id)).DriverUserId);
        Assert.Empty((await _h.Rides.GetMineAsync(second, None)).Offers);
    }

    [Fact]
    public async Task Accepting_twice_by_the_same_driver_is_not_an_error()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();

        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        var again = await _h.Rides.AcceptAsync(driver, ride.Id, None);

        Assert.True(again.IsSuccess);
    }

    [Fact]
    public async Task A_driver_who_is_not_eligible_or_not_close_or_the_wrong_class_cannot_accept()
    {
        await _h.OnDutyDriverAsync("+919000011111", plate: "PY 01 AA 0001", licence: "PY-0120200000001");
        var ride = await _h.RequestedAsync(RideVehicleClass.Auto);
        var pending = await _h.NewAccountAsync("+919000022222");
        await _h.Drivers.SaveMineAsync(pending, _h.Application(plate: "PY 01 AA 0002", licence: "PY-0120200000002"), None);
        var cab = await _h.OnDutyDriverAsync("+919000033333", RideVehicleClass.Cab, "PY 01 AA 0003", "PY-0120200000003");
        var far = await _h.OnDutyDriverAsync("+919000044444", plate: "PY 01 AA 0004", licence: "PY-0120200000004", latitude: 12.10, longitude: 79.80);
        var stranger = await _h.NewAccountAsync("+919000055555");

        Assert.Equal(RideErrors.NotAvailable, (await _h.Rides.AcceptAsync(pending, ride.Id, None)).Error);
        Assert.Equal(RideErrors.NotAvailable, (await _h.Rides.AcceptAsync(stranger, ride.Id, None)).Error);
        Assert.Equal(RideErrors.TooFar, (await _h.Rides.AcceptAsync(cab, ride.Id, None)).Error);
        Assert.Equal(RideErrors.TooFar, (await _h.Rides.AcceptAsync(far, ride.Id, None)).Error);
        Assert.Equal(RideStatus.Requested, (await _h.RideRowAsync(ride.Id)).Status);
    }

    [Fact]
    public async Task A_driver_whose_insurance_ends_while_on_duty_cannot_accept()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        _h.B.Time.Advance(TimeSpan.FromDays(400));

        var result = await _h.Rides.AcceptAsync(driver, ride.Id, None);

        Assert.Equal(RideErrors.NotAvailable, result.Error);
    }

    [Fact]
    public async Task A_driver_holds_one_ride_at_a_time()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var other = await _h.NewAccountAsync("+919000066666");
        var first = await _h.RequestedAsync();
        var second = await _h.RequestedAsync(rider: other);
        await _h.Rides.AcceptAsync(driver, first.Id, None);

        var result = await _h.Rides.AcceptAsync(driver, second.Id, None);

        Assert.Equal(RideErrors.DriverBusy, result.Error);
        var home = await _h.Rides.GetMineAsync(driver, None);
        Assert.Equal(first.Id, home.Current!.Id);
        Assert.Empty(home.Offers);
    }

    [Fact]
    public async Task A_request_nobody_took_in_time_cannot_be_accepted()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        _h.B.Time.Advance(_h.Options.SearchWindow + TimeSpan.FromSeconds(1));
        await _h.Drivers.ShareLocationAsync(driver, RideHarness.PromenadeLatitude, RideHarness.PromenadeLongitude, None);

        var result = await _h.Rides.AcceptAsync(driver, ride.Id, None);

        Assert.Equal(RideErrors.Taken, result.Error);
        Assert.Empty((await _h.Rides.GetMineAsync(driver, None)).Offers);
    }

    // --- The trip ---

    [Fact]
    public async Task A_trip_runs_from_accepted_through_arrived_and_started_to_completed_with_the_exact_cash()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        await _h.Rides.AcceptAsync(driver, ride.Id, None);

        var arrived = await _h.Rides.ArriveAsync(driver, ride.Id, None);
        var started = await _h.Rides.StartAsync(driver, ride.Id, None);
        var wrongCash = await _h.Rides.CompleteAsync(driver, ride.Id, ride.Fare - 1, None);
        var noCash = await _h.Rides.CompleteAsync(driver, ride.Id, null, None);
        var done = await _h.Rides.CompleteAsync(driver, ride.Id, ride.Fare, None);

        Assert.Equal(RideStatus.Arrived, arrived.Value!.Status);
        Assert.Equal(RideStatus.InProgress, started.Value!.Status);
        Assert.Contains("cashCollected", wrongCash.Error!.FieldErrors!.Keys);
        Assert.Contains("cashCollected", noCash.Error!.FieldErrors!.Keys);
        Assert.True(done.IsSuccess, done.Error?.Detail);
        var row = await _h.RideRowAsync(ride.Id);
        Assert.Equal(RideStatus.Completed, row.Status);
        Assert.Equal(ride.Fare, row.CashCollected);
        Assert.NotNull(row.CompletedAt);
        Assert.Equal(driver, row.DriverUserId);
    }

    [Fact]
    public async Task Steps_out_of_order_or_by_someone_else_are_refused()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111", plate: "PY 01 AA 0001", licence: "PY-0120200000001");
        var other = await _h.OnDutyDriverAsync("+919000022222", plate: "PY 01 AA 0002", licence: "PY-0120200000002");
        var ride = await _h.RequestedAsync();
        await _h.Rides.AcceptAsync(driver, ride.Id, None);

        Assert.Equal("ride_invalid_transition", (await _h.Rides.StartAsync(driver, ride.Id, None)).Error!.Code);
        Assert.Equal("ride_invalid_transition", (await _h.Rides.CompleteAsync(driver, ride.Id, ride.Fare, None)).Error!.Code);
        Assert.Equal(RideErrors.NotFound, (await _h.Rides.ArriveAsync(other, ride.Id, None)).Error);
        await _h.Rides.ArriveAsync(driver, ride.Id, None);
        Assert.Equal("ride_invalid_transition", (await _h.Rides.GiveUpAsync(driver, ride.Id, None)).Error!.Code);
        Assert.Equal(RideStatus.Arrived, (await _h.RideRowAsync(ride.Id)).Status);
    }

    [Fact]
    public async Task A_driver_who_gives_the_ride_up_before_arriving_sends_it_back_to_the_others()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111", plate: "PY 01 AA 0001", licence: "PY-0120200000001");
        var other = await _h.OnDutyDriverAsync("+919000022222", plate: "PY 01 AA 0002", licence: "PY-0120200000002");
        var ride = await _h.RequestedAsync();
        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        _h.B.Time.Advance(TimeSpan.FromMinutes(3));
        await _h.Drivers.ShareLocationAsync(other, RideHarness.PromenadeLatitude, RideHarness.PromenadeLongitude, None);

        var gaveUp = await _h.Rides.GiveUpAsync(driver, ride.Id, None);

        Assert.True(gaveUp.IsSuccess, gaveUp.Error?.Detail);
        var row = await _h.RideRowAsync(ride.Id);
        Assert.Equal(RideStatus.Requested, row.Status);
        Assert.Null(row.DriverUserId);
        Assert.Equal(_h.B.Time.GetUtcNow() + _h.Options.SearchWindow, row.SearchEndsAt);
        Assert.True((await _h.Rides.AcceptAsync(other, ride.Id, None)).IsSuccess);
    }

    [Fact]
    public async Task A_driver_at_the_pickup_can_end_the_ride_with_a_reason_and_it_is_recorded()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        var early = await _h.Rides.CancelAsDriverAsync(driver, ride.Id, "Rider did not come", None);
        await _h.Rides.ArriveAsync(driver, ride.Id, None);

        var noReason = await _h.Rides.CancelAsDriverAsync(driver, ride.Id, " ", None);
        var cancelled = await _h.Rides.CancelAsDriverAsync(driver, ride.Id, "Rider did not come", None);

        Assert.Equal("ride_invalid_transition", early.Error!.Code);
        Assert.Contains("reason", noReason.Error!.FieldErrors!.Keys);
        Assert.True(cancelled.IsSuccess);
        var row = await _h.RideRowAsync(ride.Id);
        Assert.Equal(RideStatus.Cancelled, row.Status);
        Assert.Equal(RideCancelledBy.Driver, row.CancelledBy);
        Assert.Equal("Rider did not come", row.CancelReason);
    }

    // --- Cancelling ---

    [Fact]
    public async Task A_rider_can_cancel_until_the_trip_starts_and_a_second_tap_is_not_an_error()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var searching = await _h.RequestedAsync();

        var cancelled = await _h.Rides.CancelAsync(_h.Rider, searching.Id, "Changed my mind", None);
        var again = await _h.Rides.CancelAsync(_h.Rider, searching.Id, null, None);

        Assert.Equal(RideStatus.Cancelled, cancelled.Value!.Status);
        Assert.Equal(RideCancelledBy.Rider, cancelled.Value.CancelledBy);
        Assert.Equal("Changed my mind", cancelled.Value.CancelReason);
        Assert.True(again.IsSuccess);

        var accepted = await _h.RequestedAsync();
        await _h.Rides.AcceptAsync(driver, accepted.Id, None);
        await _h.Rides.ArriveAsync(driver, accepted.Id, None);
        Assert.True((await _h.Rides.CancelAsync(_h.Rider, accepted.Id, null, None)).IsSuccess);
        Assert.Null((await _h.Rides.GetMineAsync(driver, None)).Current);

        var third = await _h.RequestedAsync();
        await _h.Rides.AcceptAsync(driver, third.Id, None);
        await _h.Rides.ArriveAsync(driver, third.Id, None);
        await _h.Rides.StartAsync(driver, third.Id, None);
        var refused = await _h.Rides.CancelAsync(_h.Rider, third.Id, null, None);
        Assert.Equal("ride_invalid_transition", refused.Error!.Code);
        Assert.Equal(RideStatus.InProgress, (await _h.RideRowAsync(third.Id)).Status);
    }

    [Fact]
    public async Task Nobody_can_cancel_someone_elses_ride()
    {
        await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        var stranger = await _h.NewAccountAsync("+919000099999");

        var result = await _h.Rides.CancelAsync(stranger, ride.Id, null, None);

        Assert.Equal(RideErrors.NotFound, result.Error);
        Assert.Equal(RideStatus.Requested, (await _h.RideRowAsync(ride.Id)).Status);
    }

    // --- Expiry ---

    [Fact]
    public async Task A_request_nobody_took_expires_and_the_rider_is_told_but_an_accepted_ride_is_left_alone()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var waiting = await _h.RequestedAsync();
        var other = await _h.NewAccountAsync("+919000066666");
        var taken = await _h.RequestedAsync(rider: other);
        await _h.Rides.AcceptAsync(driver, taken.Id, None);
        _h.Notifier.Changes.Clear();
        _h.B.Time.Advance(_h.Options.SearchWindow + TimeSpan.FromSeconds(1));

        var expired = await _h.Expiry.ExpireDueAsync(None);

        Assert.Equal(1, expired);
        var row = await _h.RideRowAsync(waiting.Id);
        Assert.Equal(RideStatus.Expired, row.Status);
        Assert.Equal(RideCancelledBy.System, row.CancelledBy);
        Assert.Equal(RideStatus.Accepted, (await _h.RideRowAsync(taken.Id)).Status);
        Assert.Contains(_h.Notifier.Changes, c => c.RideId == waiting.Id && c.Rider == _h.Rider);

        // The rider can ask again.
        await _h.Drivers.ShareLocationAsync(driver, RideHarness.PromenadeLatitude, RideHarness.PromenadeLongitude, None);
        Assert.Equal(RideStatus.Expired, (await _h.Rides.GetCurrentAsync(_h.Rider, None))!.Status);
    }

    [Fact]
    public async Task Old_unused_quotes_are_removed_and_used_ones_are_kept()
    {
        await _h.OnDutyDriverAsync("+919000011111");
        var used = await _h.RequestedAsync();
        await _h.QuotedAsync();
        _h.B.Time.Advance(RideExpiry.QuoteKeptFor + _h.Options.QuoteValidFor + TimeSpan.FromMinutes(1));

        var removed = await _h.Expiry.DeleteOldQuotesAsync(None);

        Assert.True(removed >= 3);
        var left = await _h.B.Db.RideQuotes.ToListAsync();
        Assert.Equal(used.Id, (await _h.B.Db.Rides.SingleAsync()).Id);
        Assert.Single(left);
    }

    // --- What each person sees ---

    [Fact]
    public async Task The_rider_sees_the_driver_as_a_first_name_and_a_vehicle_and_the_position_only_while_the_ride_is_active()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        var before = await _h.Rides.GetCurrentAsync(_h.Rider, None);
        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        await _h.Rides.RecordLocationAsync(driver, ride.Id, 11.95, 79.83, None);

        var active = await _h.Rides.GetCurrentAsync(_h.Rider, None);

        Assert.Null(before!.Driver);
        Assert.Null(before.DriverLocation);
        Assert.Equal("Ravi", active!.Driver!.FirstName);
        Assert.Equal("PY01AB1234", active.Driver.VehicleNumber);
        Assert.Equal(11.95, active.DriverLocation!.Latitude);
        var members = typeof(RideDriverCard).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(members, m => m.Contains("Phone") || m.Contains("Licence") || m.Contains("Permit") || m.Contains("Insurance"));

        await _h.Rides.ArriveAsync(driver, ride.Id, None);
        await _h.Rides.StartAsync(driver, ride.Id, None);
        await _h.Rides.CompleteAsync(driver, ride.Id, ride.Fare, None);
        var done = await _h.Rides.GetAsync(_h.Rider, ride.Id, None);
        Assert.Equal(RideStatus.Completed, done.Value!.Status);
        Assert.Null(done.Value.DriverLocation);
        var row = await _h.RideRowAsync(ride.Id);
        Assert.Null(row.DriverLatitude);
        Assert.Null(row.DriverLocationAt);
    }

    [Fact]
    public async Task A_position_is_accepted_only_from_the_driver_holding_the_ride_and_never_after_it_ends()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111", plate: "PY 01 AA 0001", licence: "PY-0120200000001");
        var other = await _h.OnDutyDriverAsync("+919000022222", plate: "PY 01 AA 0002", licence: "PY-0120200000002");
        var ride = await _h.RequestedAsync();

        var beforeAccept = await _h.Rides.RecordLocationAsync(driver, ride.Id, 11.95, 79.83, None);
        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        var wrongDriver = await _h.Rides.RecordLocationAsync(other, ride.Id, 11.95, 79.83, None);
        var offEarth = await _h.Rides.RecordLocationAsync(driver, ride.Id, 95, 79.83, None);
        var fine = await _h.Rides.RecordLocationAsync(driver, ride.Id, 11.95, 79.83, None);
        await _h.Rides.CancelAsync(_h.Rider, ride.Id, null, None);
        var after = await _h.Rides.RecordLocationAsync(driver, ride.Id, 11.96, 79.84, None);

        Assert.False(beforeAccept.IsSuccess);
        Assert.Equal(RideErrors.NotFound, wrongDriver.Error);
        Assert.False(offEarth.IsSuccess);
        Assert.True(fine.IsSuccess);
        Assert.False(after.IsSuccess);
        Assert.Null((await _h.RideRowAsync(ride.Id)).DriverLatitude);
    }

    [Fact]
    public async Task A_rider_sees_only_their_own_rides_newest_first()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var stranger = await _h.NewAccountAsync("+919000099999");
        var mine = await _h.RequestedAsync();
        await _h.Rides.CancelAsync(_h.Rider, mine.Id, null, None);
        _h.B.Time.Advance(TimeSpan.FromMinutes(1));
        var newer = await _h.RequestedAsync();
        await _h.RequestedAsync(rider: stranger);

        var list = await _h.Rides.ListMineAsync(_h.Rider, null, null, None);
        var notYours = await _h.Rides.GetAsync(stranger, mine.Id, None);

        Assert.Equal([newer.Id, mine.Id], list.Items.Select(r => r.Id));
        Assert.Equal(RideErrors.NotFound, notYours.Error);
        Assert.NotEqual(Guid.Empty, driver);
    }

    [Fact]
    public async Task A_finished_ride_shows_as_current_for_a_few_minutes_and_then_stops()
    {
        await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        await _h.Rides.CancelAsync(_h.Rider, ride.Id, null, None);

        var soon = await _h.Rides.GetCurrentAsync(_h.Rider, None);
        _h.B.Time.Advance(TimeSpan.FromMinutes(11));
        var later = await _h.Rides.GetCurrentAsync(_h.Rider, None);

        Assert.Equal(RideStatus.Cancelled, soon!.Status);
        Assert.Null(later);
    }

    [Fact]
    public async Task The_people_on_a_ride_are_told_when_it_changes_and_nobody_else()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        _h.Notifier.Changes.Clear();

        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        await _h.Rides.ArriveAsync(driver, ride.Id, None);
        await _h.Rides.CancelAsync(_h.Rider, ride.Id, null, None);

        Assert.Equal(3, _h.Notifier.Changes.Count);
        Assert.All(_h.Notifier.Changes, c =>
        {
            Assert.Equal(ride.Id, c.RideId);
            Assert.Equal(_h.Rider, c.Rider);
            Assert.Equal(driver, c.Driver);
        });
    }

    [Fact]
    public async Task A_failing_push_never_fails_the_ride()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var failing = new RideService(
            _h.B.Db,
            _h.B.Accounts,
            new ThrowingNotifier(),
            Microsoft.Extensions.Options.Options.Create(_h.Options),
            _h.B.Time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RideService>.Instance);
        var quote = await failing.QuoteAsync(_h.Rider, _h.Trip(), None);

        var ride = await failing.RequestAsync(_h.Rider, new RequestRideInput(quote.Value!.Options[0].QuoteId, null), None);
        var accepted = await failing.AcceptAsync(driver, ride.Value!.Id, None);

        Assert.True(ride.IsSuccess);
        Assert.True(accepted.IsSuccess);
    }

    private sealed class ThrowingNotifier : IRideNotifier
    {
        public Task RideChangedAsync(Guid rideId, Guid riderUserId, Guid? driverUserId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The hub is down.");
    }

    // --- Leaving ---

    [Fact]
    public async Task An_account_with_a_ride_that_has_not_ended_cannot_be_deleted_until_it_has()
    {
        var guard = new OnePondy.Application.Bookings.BookingsDeletionGuard(_h.B.Db);
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        Assert.DoesNotContain(OnePondy.Application.Bookings.BookingsDeletionGuard.RideInProgress, await guard.GetBlockersAsync(_h.Rider, None));

        var ride = await _h.RequestedAsync();
        Assert.Contains(OnePondy.Application.Bookings.BookingsDeletionGuard.RideInProgress, await guard.GetBlockersAsync(_h.Rider, None));
        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        Assert.Contains(OnePondy.Application.Bookings.BookingsDeletionGuard.RideInProgress, await guard.GetBlockersAsync(driver, None));

        await _h.Rides.CancelAsync(_h.Rider, ride.Id, null, None);
        Assert.DoesNotContain(OnePondy.Application.Bookings.BookingsDeletionGuard.RideInProgress, await guard.GetBlockersAsync(_h.Rider, None));
        Assert.DoesNotContain(OnePondy.Application.Bookings.BookingsDeletionGuard.RideInProgress, await guard.GetBlockersAsync(driver, None));
    }

    // --- The record itself ---

    [Fact]
    public void A_ride_refuses_every_illegal_move()
    {
        var rider = Guid.NewGuid();
        var driver = Guid.NewGuid();
        var now = DateTimeOffset.UnixEpoch;
        var quote = RideQuote.Create(rider, RideVehicleClass.Auto, new RidePlace(11.93, 79.83, "A"), new RidePlace(12.0, 79.81, "B"), 9.0m, 165m, "Free", now, TimeSpan.FromMinutes(5));

        Assert.Throws<InvalidOperationException>(() => Ride.Request(quote, null, now.AddMinutes(6), TimeSpan.FromMinutes(10)));
        var ride = Ride.Request(quote, null, now, TimeSpan.FromMinutes(10));
        Assert.Throws<InvalidOperationException>(() => ride.Arrive(driver, now));
        Assert.Throws<InvalidOperationException>(() => ride.RecordDriverLocation(driver, 11.9, 79.8, now));
        ride.Accept(driver, now);
        Assert.Throws<InvalidOperationException>(() => ride.Accept(Guid.NewGuid(), now));
        Assert.Throws<InvalidOperationException>(() => ride.Start(driver, now));
        ride.Arrive(driver, now);
        ride.Start(driver, now);
        Assert.Throws<InvalidOperationException>(() => ride.CancelByRider(rider, null, now));
        Assert.Throws<InvalidOperationException>(() => ride.Expire(now));
        Assert.Throws<ArgumentException>(() => ride.Complete(driver, 100m, now));
        ride.Complete(driver, 165m, now);
        Assert.Throws<InvalidOperationException>(() => ride.CancelBySystem("x", now));
        Assert.False(ride.IsOpen);
    }

    [Fact]
    public async Task The_module_flag_stays_off_by_default_in_the_module_list()
    {
        Assert.Contains(ModuleKeys.Rides, ModuleKeys.All);
        Assert.Equal("rides", ModuleKeys.Rides);
        Assert.False(await new RideHarness(ridesOn: false).B.Db.ModuleFlags.AnyAsync(f => f.Key == ModuleKeys.Rides));
    }
}
