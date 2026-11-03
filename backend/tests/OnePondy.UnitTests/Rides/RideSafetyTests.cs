using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Rides;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Rides;

namespace OnePondy.UnitTests.Rides;

/// <summary>Phase 9 step 9.3: SOS, incident reports and trip sharing, and the staff queue that works them.</summary>
public class RideSafetyTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly RideHarness _h = new();

    /// <summary>A rider, a driver and a ride the driver has accepted.</summary>
    private async Task<(Guid Driver, RideView Ride)> AcceptedAsync()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        var accepted = await _h.Rides.AcceptAsync(driver, ride.Id, None);
        Assert.True(accepted.IsSuccess, accepted.Error?.Detail);
        return (driver, ride);
    }

    private async Task CompleteAsync(Guid driver, RideView ride)
    {
        await _h.Rides.ArriveAsync(driver, ride.Id, None);
        await _h.Rides.StartAsync(driver, ride.Id, None);
        await _h.Rides.CompleteAsync(driver, ride.Id, ride.Fare, None);
    }

    // --- SOS ---

    [Fact]
    public async Task A_rider_can_raise_an_SOS_while_a_driver_has_the_ride_and_it_records_where_they_were()
    {
        var (_, ride) = await AcceptedAsync();

        var sos = await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, ride.Id, new RaiseSosInput(11.95, 79.83), None);

        Assert.True(sos.IsSuccess, sos.Error?.Detail);
        Assert.Equal(RideSafetyKind.Sos, sos.Value!.Kind);
        Assert.Equal(RideSafetyStatus.Open, sos.Value.Status);
        Assert.Equal("112", sos.Value.EmergencyNumber);
        var row = await _h.B.Db.RideSafetyEvents.SingleAsync();
        Assert.Equal(RideParty.Rider, row.ReportedBy);
        Assert.Equal(11.95, row.Latitude);
        Assert.Equal(RideSafetyCategory.Emergency, row.Category);
        Assert.Contains(_h.B.Db.AuditEntries, e => e.Action == AuditActions.RideSosRaised && e.ActorUserId == _h.Rider);
    }

    [Fact]
    public async Task A_driver_can_raise_an_SOS_too_and_pressing_twice_is_one_alarm()
    {
        var (driver, ride) = await AcceptedAsync();

        var first = await _h.Safety.RaiseSosAsync(driver, RideParty.Driver, ride.Id, new RaiseSosInput(null, null), None);
        var again = await _h.Safety.RaiseSosAsync(driver, RideParty.Driver, ride.Id, new RaiseSosInput(11.9, 79.8), None);

        Assert.Equal(first.Value!.Id, again.Value!.Id);
        Assert.Equal(1, await _h.B.Db.RideSafetyEvents.CountAsync());
        Assert.Null((await _h.B.Db.RideSafetyEvents.SingleAsync()).Latitude);
    }

    [Fact]
    public async Task An_SOS_needs_an_active_ride_and_a_person_on_it()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var searching = await _h.RequestedAsync();
        var stranger = await _h.NewAccountAsync("+919000099999");

        var early = await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, searching.Id, new RaiseSosInput(null, null), None);
        await _h.Rides.AcceptAsync(driver, searching.Id, None);
        var notOnIt = await _h.Safety.RaiseSosAsync(stranger, RideParty.Rider, searching.Id, new RaiseSosInput(null, null), None);
        var wrongRole = await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Driver, searching.Id, new RaiseSosInput(null, null), None);
        await CompleteAsync(driver, searching);
        var late = await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, searching.Id, new RaiseSosInput(null, null), None);

        Assert.Equal(RideSafetyErrors.NotActive, early.Error);
        Assert.Equal(RideSafetyErrors.NotOnRide, notOnIt.Error);
        Assert.Equal(RideSafetyErrors.NotOnRide, wrongRole.Error);
        Assert.Equal(RideSafetyErrors.NotActive, late.Error);
        Assert.False(await _h.B.Db.RideSafetyEvents.AnyAsync());
    }

    [Fact]
    public async Task An_SOS_with_half_a_position_or_one_off_the_Earth_is_refused()
    {
        var (_, ride) = await AcceptedAsync();

        var half = await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, ride.Id, new RaiseSosInput(11.9, null), None);
        var offEarth = await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, ride.Id, new RaiseSosInput(95, 79.8), None);

        Assert.Contains("latitude", half.Error!.FieldErrors!.Keys);
        Assert.Contains("latitude", offEarth.Error!.FieldErrors!.Keys);
        Assert.False(await _h.B.Db.RideSafetyEvents.AnyAsync());
    }

    // --- Incidents ---

    [Fact]
    public async Task Either_person_can_report_an_incident_during_the_ride_and_after_it()
    {
        var (driver, ride) = await AcceptedAsync();

        var during = await _h.Safety.ReportIncidentAsync(_h.Rider, RideParty.Rider, ride.Id, new ReportIncidentInput(RideSafetyCategory.UnsafeDriving, "  Drove too fast  "), None);
        await CompleteAsync(driver, ride);
        var after = await _h.Safety.ReportIncidentAsync(driver, RideParty.Driver, ride.Id, new ReportIncidentInput(RideSafetyCategory.Harassment, "Rider was abusive."), None);

        Assert.True(during.IsSuccess, during.Error?.Detail);
        Assert.True(after.IsSuccess, after.Error?.Detail);
        Assert.Equal(RideSafetyKind.Incident, during.Value!.Kind);
        var rows = await _h.B.Db.RideSafetyEvents.OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.Equal("Drove too fast", rows[0].Details);
        Assert.Null(rows[0].Latitude);
        Assert.Equal(RideParty.Driver, rows[1].ReportedBy);
    }

    [Fact]
    public async Task An_incident_needs_words_and_a_real_category_and_cannot_pretend_to_be_an_emergency()
    {
        var (_, ride) = await AcceptedAsync();

        var empty = await _h.Safety.ReportIncidentAsync(_h.Rider, RideParty.Rider, ride.Id, new ReportIncidentInput(null, " "), None);
        var emergency = await _h.Safety.ReportIncidentAsync(_h.Rider, RideParty.Rider, ride.Id, new ReportIncidentInput(RideSafetyCategory.Emergency, "Help"), None);
        var tooLong = await _h.Safety.ReportIncidentAsync(_h.Rider, RideParty.Rider, ride.Id, new ReportIncidentInput(RideSafetyCategory.Other, new string('x', 1001)), None);

        Assert.Contains("details", empty.Error!.FieldErrors!.Keys);
        Assert.Contains("category", empty.Error.FieldErrors.Keys);
        Assert.Contains("category", emergency.Error!.FieldErrors!.Keys);
        Assert.Contains("details", tooLong.Error!.FieldErrors!.Keys);
        Assert.False(await _h.B.Db.RideSafetyEvents.AnyAsync());
    }

    [Fact]
    public async Task An_incident_is_refused_before_a_driver_took_the_ride_and_a_month_after_it_ended_and_from_strangers()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var searching = await _h.RequestedAsync();
        var stranger = await _h.NewAccountAsync("+919000099999");
        var report = new ReportIncidentInput(RideSafetyCategory.Other, "Something happened");

        var noDriver = await _h.Safety.ReportIncidentAsync(_h.Rider, RideParty.Rider, searching.Id, report, None);
        await _h.Rides.AcceptAsync(driver, searching.Id, None);
        var notOnIt = await _h.Safety.ReportIncidentAsync(stranger, RideParty.Rider, searching.Id, report, None);
        await CompleteAsync(driver, searching);
        _h.B.Time.Advance(RideSafetyEvent.ReportWindow + TimeSpan.FromMinutes(1));
        var late = await _h.Safety.ReportIncidentAsync(_h.Rider, RideParty.Rider, searching.Id, report, None);

        Assert.Equal(RideSafetyErrors.NoDriverYet, noDriver.Error);
        Assert.Equal(RideSafetyErrors.NotOnRide, notOnIt.Error);
        Assert.Equal(RideSafetyErrors.WindowPassed, late.Error);
    }

    [Fact]
    public async Task One_person_cannot_bury_the_queue_with_reports()
    {
        var (_, ride) = await AcceptedAsync();
        for (var i = 0; i < _h.Options.MaxOpenIncidentsPerPerson; i++)
        {
            Assert.True((await _h.Safety.ReportIncidentAsync(_h.Rider, RideParty.Rider, ride.Id, new ReportIncidentInput(RideSafetyCategory.Other, $"Report {i}"), None)).IsSuccess);
        }

        var one = await _h.Safety.ReportIncidentAsync(_h.Rider, RideParty.Rider, ride.Id, new ReportIncidentInput(RideSafetyCategory.Other, "One more"), None);

        Assert.Equal(RideSafetyErrors.TooManyOpen, one.Error);
    }

    // --- Trip sharing ---

    [Fact]
    public async Task A_rider_can_share_a_trip_only_while_it_is_on_its_way()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        var stranger = await _h.NewAccountAsync("+919000099999");

        var early = await _h.Safety.CreateShareAsync(_h.Rider, ride.Id, None);
        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        var notYours = await _h.Safety.CreateShareAsync(stranger, ride.Id, None);
        var fine = await _h.Safety.CreateShareAsync(_h.Rider, ride.Id, None);

        Assert.Equal(RideSafetyErrors.ShareNotAvailable, early.Error);
        Assert.Equal(RideSafetyErrors.NotOnRide, notYours.Error);
        Assert.True(fine.IsSuccess);
        Assert.Equal(_h.B.Time.GetUtcNow() + RideShare.ValidFor, fine.Value!.ExpiresAt);
        Assert.True(fine.Value.Token.Length >= 40);
    }

    [Fact]
    public async Task Someone_with_the_link_sees_the_trip_but_only_what_is_needed()
    {
        var (driver, ride) = await AcceptedAsync();
        await _h.Rides.RecordLocationAsync(driver, ride.Id, 11.95, 79.83, None);
        var link = (await _h.Safety.CreateShareAsync(_h.Rider, ride.Id, None)).Value!;

        var shared = await _h.Safety.GetSharedAsync(link.Token, None);

        Assert.NotNull(shared);
        Assert.False(shared!.Ended);
        Assert.Equal(RideStatus.Accepted, shared.Status);
        Assert.Equal("Anita", shared.RiderFirstName);
        Assert.Equal("Ravi", shared.DriverFirstName);
        Assert.Equal("PY01AB1234", shared.VehicleNumber);
        Assert.Equal("Promenade Beach", shared.PickupLabel);
        Assert.Equal("Auroville", shared.DestinationLabel);
        Assert.Equal(11.95, shared.DriverLocation!.Latitude);

        // What the view is made of: no phone, no licence, no exact pickup point, no fare, no ids.
        var members = typeof(SharedRideView).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(members, m => m.Contains("Phone") || m.Contains("Licence") || m.Contains("Fare") || m.Contains("Id") || m.Contains("Note") || m.Contains("Pickup") && m != "PickupLabel");
    }

    [Fact]
    public async Task The_secret_is_never_stored_only_its_hash()
    {
        var (_, ride) = await AcceptedAsync();

        var link = (await _h.Safety.CreateShareAsync(_h.Rider, ride.Id, None)).Value!;

        var row = await _h.B.Db.RideShares.SingleAsync();
        Assert.Equal(_h.Hasher.Hash(link.Token), row.TokenHash);
        Assert.DoesNotContain(link.Token, Convert.ToBase64String(row.TokenHash));
    }

    [Fact]
    public async Task A_link_shows_nothing_once_the_ride_has_ended_and_never_the_driver_or_the_position()
    {
        var (driver, ride) = await AcceptedAsync();
        var link = (await _h.Safety.CreateShareAsync(_h.Rider, ride.Id, None)).Value!;
        await CompleteAsync(driver, ride);

        var shared = await _h.Safety.GetSharedAsync(link.Token, None);

        Assert.True(shared!.Ended);
        Assert.Equal(RideStatus.Completed, shared.Status);
        Assert.Null(shared.DriverFirstName);
        Assert.Null(shared.VehicleNumber);
        Assert.Null(shared.PickupLabel);
        Assert.Null(shared.DriverLocation);
        Assert.Null(shared.RiderFirstName);
    }

    [Fact]
    public async Task A_link_stops_working_when_revoked_replaced_expired_or_made_up()
    {
        var (_, ride) = await AcceptedAsync();
        var first = (await _h.Safety.CreateShareAsync(_h.Rider, ride.Id, None)).Value!;
        var second = (await _h.Safety.CreateShareAsync(_h.Rider, ride.Id, None)).Value!;

        Assert.Null(await _h.Safety.GetSharedAsync(first.Token, None));
        Assert.NotNull(await _h.Safety.GetSharedAsync(second.Token, None));

        var stranger = await _h.NewAccountAsync("+919000099999");
        Assert.Equal(RideSafetyErrors.NotOnRide, (await _h.Safety.RevokeShareAsync(stranger, ride.Id, None)).Error);
        Assert.NotNull(await _h.Safety.GetSharedAsync(second.Token, None));
        Assert.True((await _h.Safety.RevokeShareAsync(_h.Rider, ride.Id, None)).IsSuccess);
        Assert.Null(await _h.Safety.GetSharedAsync(second.Token, None));

        var third = (await _h.Safety.CreateShareAsync(_h.Rider, ride.Id, None)).Value!;
        _h.B.Time.Advance(RideShare.ValidFor + TimeSpan.FromSeconds(1));
        Assert.Null(await _h.Safety.GetSharedAsync(third.Token, None));

        Assert.Null(await _h.Safety.GetSharedAsync(null, None));
        Assert.Null(await _h.Safety.GetSharedAsync("", None));
        Assert.Null(await _h.Safety.GetSharedAsync("not-a-real-link", None));
        Assert.Null(await _h.Safety.GetSharedAsync(new string('a', 500), None));
    }

    // --- The staff queue ---

    [Fact]
    public async Task The_queue_puts_open_emergencies_first_then_the_oldest_and_counts_what_is_waiting()
    {
        var (driver, ride) = await AcceptedAsync();
        await _h.Safety.ReportIncidentAsync(_h.Rider, RideParty.Rider, ride.Id, new ReportIncidentInput(RideSafetyCategory.Other, "First, an incident"), None);
        _h.B.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Safety.RaiseSosAsync(driver, RideParty.Driver, ride.Id, new RaiseSosInput(11.9, 79.8), None);
        _h.B.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Safety.ReportIncidentAsync(driver, RideParty.Driver, ride.Id, new ReportIncidentInput(RideSafetyCategory.Overcharge, "Second, an incident"), None);

        var list = await _h.Safety.ListAsync(new SafetyListQuery(null, null, null), None);
        var counts = await _h.Safety.CountsAsync(None);

        Assert.Equal([RideSafetyKind.Sos, RideSafetyKind.Incident, RideSafetyKind.Incident], list.Items.Select(i => i.Kind));
        Assert.Equal("First, an incident", list.Items[1].Details);
        Assert.Equal(1, counts.OpenSos);
        Assert.Equal(2, counts.OpenIncidents);
        Assert.Equal(_h.B.Time.GetUtcNow().AddMinutes(-2), counts.OldestOpenAt);
    }

    [Fact]
    public async Task Opening_a_report_shows_both_people_and_how_to_reach_them_and_is_recorded()
    {
        var (_, ride) = await AcceptedAsync();
        var sos = (await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, ride.Id, new RaiseSosInput(11.95, 79.83), None)).Value!;

        var detail = await _h.Safety.GetAsync(sos.Id, _h.B.Administrator, None);

        Assert.True(detail.IsSuccess, detail.Error?.Detail);
        Assert.Equal(11.95, detail.Value!.Latitude);
        Assert.Equal("Anita Raman", detail.Value.Rider.Name);
        Assert.Equal("+919876543210", detail.Value.Rider.PhoneNumber);
        Assert.Equal("Ravi Kumar", detail.Value.Driver!.Name);
        Assert.Equal("PY01AB1234", detail.Value.Ride.VehicleNumber);
        Assert.Contains(_h.B.Db.AuditEntries, e => e.Action == AuditActions.RideSafetyViewed && e.ActorUserId == _h.B.Administrator && e.EntityId == sos.Id.ToString());
        Assert.Equal(RideSafetyErrors.NotFound, (await _h.Safety.GetAsync(Guid.NewGuid(), _h.B.Administrator, None)).Error);
    }

    [Fact]
    public async Task Staff_take_a_report_and_close_it_with_a_note_and_every_step_is_audited()
    {
        var (_, ride) = await AcceptedAsync();
        var sos = (await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, ride.Id, new RaiseSosInput(null, null), None)).Value!;
        var staff = _h.B.Administrator;

        var taken = await _h.Safety.AcknowledgeAsync(sos.Id, 0, staff, None);
        var again = await _h.Safety.AcknowledgeAsync(sos.Id, 0, staff, None);
        var noNote = await _h.Safety.ResolveAsync(sos.Id, new ResolveSafetyInput(0, " "), staff, None);
        var closed = await _h.Safety.ResolveAsync(sos.Id, new ResolveSafetyInput(0, "Called both; rider was safe."), staff, None);
        var twice = await _h.Safety.ResolveAsync(sos.Id, new ResolveSafetyInput(0, "Again"), staff, None);

        Assert.Equal(RideSafetyStatus.Acknowledged, taken.Value!.Status);
        Assert.True(again.IsSuccess);
        Assert.Contains("note", noNote.Error!.FieldErrors!.Keys);
        Assert.Equal(RideSafetyStatus.Resolved, closed.Value!.Status);
        Assert.Equal("Called both; rider was safe.", closed.Value.ResolutionNote);
        Assert.Equal("ride_safety_invalid_transition", twice.Error!.Code);
        var actions = _h.B.Db.AuditEntries.Select(e => e.Action).ToList();
        Assert.Contains(AuditActions.RideSafetyAcknowledged, actions);
        Assert.Contains(AuditActions.RideSafetyResolved, actions);
        Assert.Equal(0, (await _h.Safety.CountsAsync(None)).OpenSos);
    }

    [Fact]
    public async Task A_resolved_emergency_lets_the_person_raise_another_on_the_same_ride()
    {
        var (_, ride) = await AcceptedAsync();
        var first = (await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, ride.Id, new RaiseSosInput(null, null), None)).Value!;
        await _h.Safety.ResolveAsync(first.Id, new ResolveSafetyInput(0, "False alarm"), _h.B.Administrator, None);

        var second = await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, ride.Id, new RaiseSosInput(null, null), None);

        Assert.NotEqual(first.Id, second.Value!.Id);
        var resolved = await _h.Safety.ListAsync(new SafetyListQuery("resolved", null, null), None);
        Assert.Single(resolved.Items);
        Assert.Equal(2, (await _h.Safety.ListAsync(new SafetyListQuery("all", null, null), None)).TotalCount);
    }

    [Fact]
    public async Task A_stale_version_is_refused()
    {
        var (_, ride) = await AcceptedAsync();
        var sos = (await _h.Safety.RaiseSosAsync(_h.Rider, RideParty.Rider, ride.Id, new RaiseSosInput(null, null), None)).Value!;

        var stale = await _h.Safety.AcknowledgeAsync(sos.Id, 5, _h.B.Administrator, None);

        Assert.Equal(RideSafetyErrors.Changed, stale.Error);
    }
}
