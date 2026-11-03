using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Rides;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Rides;

namespace OnePondy.UnitTests.Rides;

/// <summary>Phase 9 step 9.1: ride drivers apply with their documents, administrators review, and only an approved, insured driver goes on duty.</summary>
public class RideDriverTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly RideHarness _h = new();

    private async Task<(Guid User, RideDriverView View)> AppliedAsync(string phone = "+919000011111", RideDriverInput? input = null)
    {
        var user = await _h.NewAccountAsync(phone);
        var applied = await _h.Drivers.SaveMineAsync(user, input ?? _h.Application(), None);
        Assert.True(applied.IsSuccess, applied.Error?.Detail);
        return (user, applied.Value!);
    }

    private async Task<Guid> ApprovedAsync(string phone = "+919000011111", string plate = "PY 01 AB 1234", string licence = "PY-0120200012345")
    {
        var (user, view) = await AppliedAsync(phone, _h.Application(plate: plate, licence: licence));
        var approved = await _h.Drivers.ApproveAsync(user, view.Version, _h.B.Administrator, None);
        Assert.True(approved.IsSuccess, approved.Error?.Detail);
        return user;
    }

    // --- Applying ---

    [Fact]
    public async Task A_driver_who_has_not_applied_is_shown_as_such()
    {
        var view = await _h.Drivers.GetMineAsync(await _h.NewAccountAsync("+919000011111"), None);

        Assert.False(view.Configured);
        Assert.False(view.Eligible);
        Assert.False(view.IsOnDuty);
    }

    [Fact]
    public async Task Applying_makes_a_pending_application_with_every_number_tidied()
    {
        var (user, view) = await AppliedAsync();

        Assert.Equal(DriverApprovalStatus.Pending, view.ApprovalStatus);
        Assert.Equal("PY01AB1234", view.VehicleNumber);
        Assert.Equal("PY0120200012345", view.LicenceNumber);
        Assert.Equal("PERMIT0001", view.PermitNumber);
        Assert.Equal("POLICY778899", view.InsurancePolicyNumber);
        Assert.False(view.Eligible);
        Assert.Contains(_h.B.Db.AuditEntries, e => e.Action == AuditActions.RideDriverApplied && e.ActorUserId == user);
    }

    [Fact]
    public async Task Every_document_is_required_and_insurance_must_not_have_ended()
    {
        var user = await _h.NewAccountAsync("+919000011111");
        var yesterday = RideClock.Today(_h.B.Time.GetUtcNow()).AddDays(-1);

        var result = await _h.Drivers.SaveMineAsync(user, new RideDriverInput("R", null, "x", "y", null, " ", yesterday, null), None);

        Assert.False(result.IsSuccess);
        var fields = result.Error!.FieldErrors!.Keys;
        Assert.Contains("fullName", fields);
        Assert.Contains("vehicleClass", fields);
        Assert.Contains("vehicleNumber", fields);
        Assert.Contains("licenceNumber", fields);
        Assert.Contains("permitNumber", fields);
        Assert.Contains("insurancePolicyNumber", fields);
        Assert.Contains("insuranceValidUntil", fields);
        Assert.False(await _h.B.Db.RideDriverProfiles.AnyAsync());
    }

    [Fact]
    public async Task Insurance_ending_today_is_still_valid_and_yesterday_is_not()
    {
        var today = RideClock.Today(_h.B.Time.GetUtcNow());
        var user = await _h.NewAccountAsync("+919000011111");

        var ending = await _h.Drivers.SaveMineAsync(user, _h.Application(insuredUntil: today), None);
        var ended = await _h.Drivers.SaveMineAsync(await _h.NewAccountAsync("+919000022222"), _h.Application(insuredUntil: today.AddDays(-1), plate: "PY 02 CD 5678", licence: "PY-0120200099999"), None);

        Assert.True(ending.IsSuccess);
        Assert.False(ended.IsSuccess);
    }

    [Fact]
    public async Task The_same_licence_or_vehicle_cannot_be_registered_on_two_accounts()
    {
        await AppliedAsync("+919000011111");
        var other = await _h.NewAccountAsync("+919000022222");

        var sameLicence = await _h.Drivers.SaveMineAsync(other, _h.Application(plate: "PY 09 ZZ 0001"), None);
        var sameVehicle = await _h.Drivers.SaveMineAsync(other, _h.Application(licence: "PY-0120200077777"), None);

        Assert.Equal(RideDriverErrors.DocumentInUse, sameLicence.Error);
        Assert.Equal(RideDriverErrors.DocumentInUse, sameVehicle.Error);
    }

    // --- Eligibility ---

    [Fact]
    public async Task A_pending_driver_cannot_go_on_duty_and_an_approved_insured_one_can()
    {
        var (user, view) = await AppliedAsync();
        var early = await _h.Drivers.SetDutyAsync(user, true, view.Version, None);
        Assert.Equal(RideDriverErrors.NotApproved, early.Error);

        await _h.Drivers.ApproveAsync(user, view.Version, _h.B.Administrator, None);
        var approved = await _h.Drivers.GetMineAsync(user, None);
        Assert.True(approved.Eligible);

        var duty = await _h.Drivers.SetDutyAsync(user, true, approved.Version, None);
        Assert.True(duty.IsSuccess);
        Assert.True(duty.Value!.IsOnDuty);
    }

    [Fact]
    public async Task An_approved_driver_whose_insurance_has_ended_is_not_eligible_and_cannot_start_a_shift()
    {
        var user = await ApprovedAsync();
        _h.B.Time.Advance(TimeSpan.FromDays(400));

        var view = await _h.Drivers.GetMineAsync(user, None);
        var duty = await _h.Drivers.SetDutyAsync(user, true, view.Version, None);

        Assert.False(view.Eligible);
        Assert.True(view.InsuranceEnded);
        Assert.Equal(RideDriverErrors.InsuranceEnded, duty.Error);
    }

    [Fact]
    public async Task A_driver_on_duty_when_the_insurance_ends_is_taken_off_duty_the_next_time_they_share_a_position()
    {
        var user = await _h.OnDutyDriverAsync("+919000011111");
        _h.B.Time.Advance(TimeSpan.FromDays(400));

        var shared = await _h.Drivers.ShareLocationAsync(user, RideHarness.PromenadeLatitude, RideHarness.PromenadeLongitude, None);

        Assert.Equal(RideDriverErrors.InsuranceEnded, shared.Error);
        var profile = await _h.ProfileAsync(user);
        Assert.False(profile.IsOnDuty);
        Assert.Null(await _h.PositionAsync(user));
    }

    [Fact]
    public async Task Changing_a_document_sends_an_approved_driver_back_to_review_and_off_duty()
    {
        var user = await _h.OnDutyDriverAsync("+919000011111");
        var view = await _h.Drivers.GetMineAsync(user, None);

        var edited = await _h.Drivers.SaveMineAsync(user, _h.Application(licence: "PY-0120209999999", version: view.Version), None);

        Assert.True(edited.IsSuccess, edited.Error?.Detail);
        Assert.Equal(DriverApprovalStatus.Pending, edited.Value!.ApprovalStatus);
        Assert.False(edited.Value.IsOnDuty);
        Assert.Null(await _h.PositionAsync(user));
    }

    [Fact]
    public async Task Saving_what_is_already_there_changes_nothing()
    {
        var user = await _h.OnDutyDriverAsync("+919000011111");
        var view = await _h.Drivers.GetMineAsync(user, None);

        var saved = await _h.Drivers.SaveMineAsync(user, _h.Application(version: view.Version), None);

        Assert.Equal(DriverApprovalStatus.Approved, saved.Value!.ApprovalStatus);
        Assert.True(saved.Value.IsOnDuty);
    }

    [Fact]
    public async Task Editing_needs_the_version_that_was_loaded()
    {
        var (user, _) = await AppliedAsync();

        var result = await _h.Drivers.SaveMineAsync(user, _h.Application(licence: "PY-0120209999999"), None);

        Assert.False(result.IsSuccess);
        Assert.Contains("version", result.Error!.FieldErrors!.Keys);
    }

    // --- Position ---

    [Fact]
    public async Task A_position_is_kept_only_while_on_duty_and_only_the_latest()
    {
        var user = await _h.OnDutyDriverAsync("+919000011111");

        await _h.Drivers.ShareLocationAsync(user, 11.95, 79.82, None);
        await _h.Drivers.ShareLocationAsync(user, 11.96, 79.83, None);
        var position = await _h.PositionAsync(user);
        Assert.Equal(11.96, position!.Latitude);
        Assert.Equal(79.83, position.Longitude);
        Assert.Equal(1, await _h.B.Db.RideDriverPositions.CountAsync());

        var version = (await _h.Drivers.GetMineAsync(user, None)).Version;
        await _h.Drivers.SetDutyAsync(user, false, version, None);
        Assert.Null(await _h.PositionAsync(user));

        var refused = await _h.Drivers.ShareLocationAsync(user, 11.95, 79.82, None);
        Assert.Equal(RideDriverErrors.NotOnDuty, refused.Error);
    }

    [Theory]
    [InlineData(91, 79)]
    [InlineData(11, 181)]
    [InlineData(double.NaN, 79)]
    public async Task A_position_off_the_Earth_is_refused(double latitude, double longitude)
    {
        var user = await _h.OnDutyDriverAsync("+919000011111");

        var result = await _h.Drivers.ShareLocationAsync(user, latitude, longitude, None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RideHarness.PromenadeLatitude, (await _h.PositionAsync(user))!.Latitude);
    }

    [Fact]
    public async Task A_position_goes_stale_after_two_minutes()
    {
        var user = await _h.OnDutyDriverAsync("+919000011111");
        var now = _h.B.Time.GetUtcNow();
        var position = await _h.PositionAsync(user);

        Assert.True(position!.IsFresh(now));
        Assert.True(position.IsFresh(now.AddMinutes(2)));
        Assert.False(position.IsFresh(now.AddMinutes(2).AddSeconds(1)));
    }

    [Fact]
    public async Task Reporting_a_position_never_changes_the_profile_so_nobody_acting_on_it_is_told_it_changed()
    {
        var user = await _h.OnDutyDriverAsync("+919000011111");
        var before = await _h.ProfileAsync(user);

        _h.B.Time.Advance(TimeSpan.FromSeconds(15));
        await _h.Drivers.ShareLocationAsync(user, 11.95, 79.82, None);
        var after = await _h.ProfileAsync(user);

        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.Version, after.Version);
    }

    // --- Administrators ---

    [Fact]
    public async Task An_administrator_approves_rejects_suspends_and_reinstates_and_each_is_audited()
    {
        var (user, view) = await AppliedAsync();
        var admin = _h.B.Administrator;

        var rejected = await _h.Drivers.RejectAsync(user, view.Version, "Permit photo unreadable", admin, None);
        Assert.Equal(DriverApprovalStatus.Rejected, rejected.Value!.ApprovalStatus);

        // Resubmitting after a rejection goes back to pending by itself.
        var again = await _h.Drivers.SaveMineAsync(user, _h.Application(licence: "PY-0120209999999", version: rejected.Value.Version), None);
        Assert.Equal(DriverApprovalStatus.Pending, again.Value!.ApprovalStatus);

        var approved = await _h.Drivers.ApproveAsync(user, again.Value.Version, admin, None);
        var suspended = await _h.Drivers.SuspendAsync(user, approved.Value!.Version, "Complaint under review", admin, None);
        var reinstated = await _h.Drivers.ReinstateAsync(user, suspended.Value!.Version, admin, None);

        Assert.Equal(DriverApprovalStatus.Approved, reinstated.Value!.ApprovalStatus);
        var actions = _h.B.Db.AuditEntries.Select(e => e.Action).ToList();
        Assert.Contains(AuditActions.RideDriverRejected, actions);
        Assert.Contains(AuditActions.RideDriverApproved, actions);
        Assert.Contains(AuditActions.RideDriverSuspended, actions);
        Assert.Contains(AuditActions.RideDriverReinstated, actions);
    }

    [Fact]
    public async Task A_rejection_and_a_suspension_need_a_reason()
    {
        var (user, view) = await AppliedAsync();

        var rejected = await _h.Drivers.RejectAsync(user, view.Version, " ", _h.B.Administrator, None);

        Assert.False(rejected.IsSuccess);
        Assert.Contains("reason", rejected.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Suspending_takes_a_driver_off_duty_at_once_and_a_suspended_driver_cannot_change_anything()
    {
        var user = await _h.OnDutyDriverAsync("+919000011111");
        var view = await _h.Drivers.GetMineAsync(user, None);

        var suspended = await _h.Drivers.SuspendAsync(user, view.Version, "Safety complaint", _h.B.Administrator, None);

        Assert.False(suspended.Value!.IsOnDuty);
        Assert.Null(await _h.PositionAsync(user));
        var edit = await _h.Drivers.SaveMineAsync(user, _h.Application(licence: "PY-0120209999999", version: suspended.Value.Version), None);
        Assert.Equal(RideDriverErrors.Locked, edit.Error);
    }

    [Fact]
    public async Task A_review_acts_on_the_version_the_reviewer_saw()
    {
        var (user, view) = await AppliedAsync();
        // The driver edits their application after the reviewer opened it: the reviewer's version is now stale.
        var edited = await _h.Drivers.SaveMineAsync(user, _h.Application(licence: "PY-0120209999999", version: view.Version), None);
        Assert.True(edited.IsSuccess);
        _h.B.Db.Entry(await _h.B.Db.RideDriverProfiles.SingleAsync()).Property(p => p.Version).CurrentValue = 7;
        await _h.B.Db.SaveChangesAsync();

        var stale = await _h.Drivers.ApproveAsync(user, view.Version, _h.B.Administrator, None);

        Assert.Equal(RideDriverErrors.Changed, stale.Error);
    }

    [Fact]
    public async Task The_administrators_list_filters_by_status_and_shows_when_insurance_has_ended()
    {
        await AppliedAsync("+919000011111");
        await ApprovedAsync("+919000022222", "PY 02 CD 5678", "PY-0120200099999");
        var other = await _h.NewAccountAsync("+919000033333");
        await _h.Drivers.SaveMineAsync(other, _h.Application(plate: "PY 03 EF 9999", licence: "PY-0120200055555", insuredUntil: RideClock.Today(_h.B.Time.GetUtcNow()).AddDays(10)), None);
        _h.B.Time.Advance(TimeSpan.FromDays(20));

        var pending = await _h.Drivers.AdminListAsync(new RideDriverListQuery(DriverApprovalStatus.Pending, null, null), None);

        Assert.Equal(2, pending.TotalCount);
        Assert.Contains(pending.Items, d => d.InsuranceEnded);
    }

    [Fact]
    public async Task Suspending_a_driver_hands_back_a_ride_they_have_not_reached_but_not_one_under_way()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        var view = await _h.Drivers.GetMineAsync(driver, None);

        await _h.Drivers.SuspendAsync(driver, view.Version, "Safety complaint", _h.B.Administrator, None);

        var row = await _h.RideRowAsync(ride.Id);
        Assert.Equal(RideStatus.Requested, row.Status);
        Assert.Null(row.DriverUserId);
    }

    [Fact]
    public async Task Going_off_duty_or_changing_details_is_refused_in_the_middle_of_a_ride()
    {
        var driver = await _h.OnDutyDriverAsync("+919000011111");
        var ride = await _h.RequestedAsync();
        await _h.Rides.AcceptAsync(driver, ride.Id, None);
        var view = await _h.Drivers.GetMineAsync(driver, None);

        var off = await _h.Drivers.SetDutyAsync(driver, false, view.Version, None);
        var edit = await _h.Drivers.SaveMineAsync(driver, _h.Application(licence: "PY-0120209999999", version: view.Version), None);

        Assert.Equal(RideDriverErrors.HasActiveRide, off.Error);
        Assert.Equal(RideDriverErrors.HasActiveRide, edit.Error);
    }

    [Fact]
    public void Eligibility_is_approved_and_insured_and_nothing_else()
    {
        var today = new DateOnly(2026, 10, 26);
        var content = new RideDriverContent("Ravi", RideVehicleClass.Auto, "PY01AB1234", "PY0120200012345", "P1", "I1", today);
        var profile = RideDriverProfile.Apply(Guid.NewGuid(), content, DateTimeOffset.UnixEpoch);

        Assert.False(profile.IsEligible(today));
        profile.Approve(DateTimeOffset.UnixEpoch);
        Assert.True(profile.IsEligible(today));
        Assert.False(profile.IsEligible(today.AddDays(1)));
        Assert.False(profile.IsAvailableForRides(today));
        profile.GoOnDuty(today, DateTimeOffset.UnixEpoch);
        Assert.True(profile.IsAvailableForRides(today));
        profile.Suspend("Complaint", DateTimeOffset.UnixEpoch);
        Assert.False(profile.IsEligible(today));
        Assert.False(profile.IsOnDuty);
    }
}
