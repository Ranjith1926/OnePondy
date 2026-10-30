using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Bookings;
using OnePondy.Application.Delivery;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Identity;
using OnePondy.UnitTests.Businesses;

namespace OnePondy.UnitTests.Delivery;

/// <summary>Phase 8 step 8.6: drivers apply, administrators review, approved drivers start and end shifts.</summary>
public class DriverTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly BusinessHarness _b = new();
    private readonly DriverService _drivers;
    private readonly Guid _driver;
    private readonly Guid _otherDriver;

    public DriverTests()
    {
        _drivers = new DriverService(_b.Db, _b.Accounts, _b.Audit, _b.Time);
        _driver = _b.Customer;
        _otherDriver = _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000011111", None).Result.Id;
    }

    private static DriverInput Scooter(
        string name = "Ravi Kumar",
        string plate = "PY 01 AB 1234",
        string licence = "PY-0120200012345",
        uint? version = null) =>
        new(name, VehicleType.Scooter, plate, licence, version);

    private static DriverInput Bicycle(string name = "Meena S", uint? version = null) =>
        new(name, VehicleType.Bicycle, null, null, version);

    private async Task<DriverProfileView> AppliedAsync(Guid? user = null, DriverInput? input = null)
    {
        var applied = await _drivers.SaveMineAsync(user ?? _driver, input ?? Scooter(), None);
        Assert.True(applied.IsSuccess, applied.Error?.Detail);
        return applied.Value!;
    }

    private async Task<DriverProfileView> ApprovedAsync(Guid? user = null, DriverInput? input = null)
    {
        var applied = await AppliedAsync(user, input);
        var approved = await _drivers.ApproveAsync(user ?? _driver, applied.Version, _b.Administrator, None);
        Assert.True(approved.IsSuccess, approved.Error?.Detail);
        return await _drivers.GetMineAsync(user ?? _driver, None);
    }

    // --- Applying ---

    [Fact]
    public async Task A_driver_who_has_not_applied_is_shown_as_such()
    {
        var view = await _drivers.GetMineAsync(_driver, None);

        Assert.False(view.Configured);
        Assert.Null(view.ApprovalStatus);
        Assert.False(view.IsOnDuty);
        Assert.False(view.CanGoOnDuty);
    }

    [Fact]
    public async Task Applying_makes_a_pending_application_with_the_plate_and_licence_tidied()
    {
        var view = await AppliedAsync();

        Assert.True(view.Configured);
        Assert.Equal(DriverApprovalStatus.Pending, view.ApprovalStatus);
        Assert.Equal("Ravi Kumar", view.FullName);
        Assert.Equal("PY01AB1234", view.VehicleNumber);
        Assert.Equal("PY0120200012345", view.LicenceNumber);
        Assert.False(view.CanGoOnDuty);
        Assert.Contains(_b.Db.AuditEntries, e => e.Action == AuditActions.DriverApplied && e.ActorUserId == _driver);
    }

    [Fact]
    public async Task A_bicycle_needs_no_registration_or_licence_and_any_given_are_not_kept()
    {
        var view = await AppliedAsync(input: new DriverInput("Meena S", VehicleType.Bicycle, "PY01AB1234", "PY0120200012345", null));

        Assert.Equal(VehicleType.Bicycle, view.VehicleType);
        Assert.Null(view.VehicleNumber);
        Assert.Null(view.LicenceNumber);
    }

    [Fact]
    public async Task A_motorised_vehicle_needs_its_registration_and_licence()
    {
        var missing = await _drivers.SaveMineAsync(_driver, new DriverInput("Ravi Kumar", VehicleType.Motorbike, null, null, null), None);
        var malformed = await _drivers.SaveMineAsync(_driver, Scooter(plate: "A!", licence: "123"), None);

        Assert.Equal(["licenceNumber", "vehicleNumber"], missing.Error!.FieldErrors!.Keys.Order());
        Assert.Equal(["licenceNumber", "vehicleNumber"], malformed.Error!.FieldErrors!.Keys.Order());
        Assert.False((await _drivers.GetMineAsync(_driver, None)).Configured);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("R")]
    [InlineData("Ravi\u0007Kumar")]
    public async Task A_name_is_required_and_must_be_a_real_one(string name)
    {
        var result = await _drivers.SaveMineAsync(_driver, Scooter(name: name), None);

        Assert.Contains("fullName", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task A_vehicle_type_must_be_chosen()
    {
        var result = await _drivers.SaveMineAsync(_driver, new DriverInput("Ravi Kumar", null, null, null, null), None);

        Assert.Contains("vehicleType", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task One_licence_cannot_be_registered_on_two_accounts_however_it_is_typed()
    {
        await AppliedAsync();

        var second = await _drivers.SaveMineAsync(_otherDriver, Scooter(name: "Someone Else", licence: "py 0120200012345"), None);

        Assert.Equal("driver_licence_in_use", second.Error!.Code);
        Assert.False((await _drivers.GetMineAsync(_otherDriver, None)).Configured);
    }

    [Fact]
    public async Task A_driver_can_save_their_own_licence_again()
    {
        var first = await AppliedAsync();

        var again = await _drivers.SaveMineAsync(_driver, Scooter(name: "Ravi K. Kumar", version: first.Version), None);

        Assert.True(again.IsSuccess, again.Error?.Detail);
        Assert.Equal("Ravi K. Kumar", again.Value!.FullName);
    }

    [Fact]
    public async Task An_edit_of_an_existing_application_must_say_which_version_it_is_based_on()
    {
        await AppliedAsync();

        var result = await _drivers.SaveMineAsync(_driver, Scooter(name: "Ravi K"), None);

        Assert.Contains("version", result.Error!.FieldErrors!.Keys);
    }

    // --- Editing after review ---

    [Fact]
    public async Task A_rejected_application_goes_back_for_review_when_it_is_corrected()
    {
        var applied = await AppliedAsync();
        await _drivers.RejectAsync(_driver, applied.Version, "Licence photo unreadable", _b.Administrator, None);
        var rejected = await _drivers.GetMineAsync(_driver, None);
        Assert.Equal("Licence photo unreadable", rejected.ReviewNote);

        var fixedUp = await _drivers.SaveMineAsync(_driver, Scooter(licence: "PY0120200099999", version: rejected.Version), None);

        Assert.Equal(DriverApprovalStatus.Pending, fixedUp.Value!.ApprovalStatus);
        Assert.Null(fixedUp.Value.ReviewNote);
    }

    [Fact]
    public async Task An_approved_driver_who_changes_their_vehicle_goes_back_for_review_and_off_duty()
    {
        var approved = await ApprovedAsync();
        await _drivers.SetDutyAsync(_driver, true, approved.Version, None);
        var onDuty = await _drivers.GetMineAsync(_driver, None);
        Assert.True(onDuty.IsOnDuty);

        var changed = await _drivers.SaveMineAsync(_driver, Scooter(plate: "PY01ZZ9999", version: onDuty.Version), None);

        Assert.Equal(DriverApprovalStatus.Pending, changed.Value!.ApprovalStatus);
        Assert.False(changed.Value.IsOnDuty);
        Assert.Null(changed.Value.OnDutySince);
        Assert.False(changed.Value.CanGoOnDuty);
    }

    [Fact]
    public async Task Saving_exactly_what_is_already_there_changes_nothing()
    {
        var approved = await ApprovedAsync();
        await _drivers.SetDutyAsync(_driver, true, approved.Version, None);
        var onDuty = await _drivers.GetMineAsync(_driver, None);

        var same = await _drivers.SaveMineAsync(_driver, Scooter(plate: "py-01-ab-1234", version: onDuty.Version), None);

        Assert.Equal(DriverApprovalStatus.Approved, same.Value!.ApprovalStatus);
        Assert.True(same.Value.IsOnDuty);
    }

    [Fact]
    public async Task A_suspended_driver_cannot_change_anything()
    {
        var approved = await ApprovedAsync();
        await _drivers.SuspendAsync(_driver, approved.Version, "Complaint under review", _b.Administrator, None);
        var suspended = await _drivers.GetMineAsync(_driver, None);

        var edit = await _drivers.SaveMineAsync(_driver, Scooter(name: "New Name", version: suspended.Version), None);

        Assert.Equal("driver_locked", edit.Error!.Code);
    }

    // --- Duty ---

    [Fact]
    public async Task Only_an_approved_driver_can_go_on_duty()
    {
        var pending = await AppliedAsync();

        var refused = await _drivers.SetDutyAsync(_driver, true, pending.Version, None);

        Assert.Equal("driver_not_approved", refused.Error!.Code);
        Assert.False((await _drivers.GetMineAsync(_driver, None)).IsOnDuty);
        Assert.False(await _drivers.IsAvailableForJobsAsync(_driver, None));
    }

    [Fact]
    public async Task An_approved_driver_starts_and_ends_a_shift_and_is_available_for_jobs_only_while_on()
    {
        var approved = await ApprovedAsync();
        Assert.False(await _drivers.IsAvailableForJobsAsync(_driver, None));

        var on = (await _drivers.SetDutyAsync(_driver, true, approved.Version, None)).Value!;
        Assert.True(on.IsOnDuty);
        Assert.Equal(_b.Time.GetUtcNow(), on.OnDutySince);
        Assert.True(await _drivers.IsAvailableForJobsAsync(_driver, None));

        var off = (await _drivers.SetDutyAsync(_driver, false, on.Version, None)).Value!;
        Assert.False(off.IsOnDuty);
        Assert.Null(off.OnDutySince);
        Assert.False(await _drivers.IsAvailableForJobsAsync(_driver, None));
    }

    [Fact]
    public async Task Going_on_duty_twice_keeps_the_original_start_time()
    {
        var approved = await ApprovedAsync();
        var first = (await _drivers.SetDutyAsync(_driver, true, approved.Version, None)).Value!;
        var started = first.OnDutySince;
        _b.Time.Advance(TimeSpan.FromHours(2));

        var again = (await _drivers.SetDutyAsync(_driver, true, first.Version, None)).Value!;

        Assert.Equal(started, again.OnDutySince);
    }

    [Fact]
    public async Task Ending_a_shift_is_always_allowed_even_before_approval()
    {
        var pending = await AppliedAsync();

        var off = await _drivers.SetDutyAsync(_driver, false, pending.Version, None);

        Assert.True(off.IsSuccess);
        Assert.False(off.Value!.IsOnDuty);
    }

    [Fact]
    public async Task Duty_needs_an_application_and_the_current_version()
    {
        Assert.Equal("driver_not_applied", (await _drivers.SetDutyAsync(_driver, true, 0, None)).Error!.Code);

        var approved = await ApprovedAsync();
        var stale = await _drivers.SetDutyAsync(_driver, true, approved.Version + 999, None);

        Assert.Equal("driver_changed", stale.Error!.Code);
        Assert.False((await _drivers.GetMineAsync(_driver, None)).IsOnDuty);
    }

    [Fact]
    public async Task Suspending_a_driver_takes_them_off_duty_at_once_and_reinstating_does_not_put_them_back()
    {
        var approved = await ApprovedAsync();
        var on = (await _drivers.SetDutyAsync(_driver, true, approved.Version, None)).Value!;

        var suspended = await _drivers.SuspendAsync(_driver, on.Version, "Safety complaint", _b.Administrator, None);
        Assert.False(suspended.Value!.IsOnDuty);
        Assert.False(await _drivers.IsAvailableForJobsAsync(_driver, None));
        Assert.Equal("driver_not_approved", (await _drivers.SetDutyAsync(_driver, true, suspended.Value.Version, None)).Error!.Code);

        var reinstated = await _drivers.ReinstateAsync(_driver, suspended.Value.Version, _b.Administrator, None);

        Assert.Equal(DriverApprovalStatus.Approved, reinstated.Value!.ApprovalStatus);
        Assert.False(reinstated.Value.IsOnDuty);
    }

    [Fact]
    public async Task One_driver_going_on_duty_does_not_affect_another()
    {
        var mine = await ApprovedAsync();
        var theirs = await ApprovedAsync(_otherDriver, Bicycle());

        await _drivers.SetDutyAsync(_driver, true, mine.Version, None);

        Assert.False((await _drivers.GetMineAsync(_otherDriver, None)).IsOnDuty);
        Assert.True(theirs.CanGoOnDuty);
    }

    // --- Review by an administrator ---

    [Fact]
    public async Task An_administrator_sees_the_application_with_the_drivers_contact_details()
    {
        await AppliedAsync();

        var detail = (await _drivers.AdminGetAsync(_driver, None)).Value!;

        Assert.Equal("Ravi Kumar", detail.FullName);
        Assert.Equal("PY01AB1234", detail.VehicleNumber);
        Assert.Equal("+919876543210", detail.PhoneNumber);
        Assert.Equal("driver_not_found", (await _drivers.AdminGetAsync(Guid.NewGuid(), None)).Error!.Code);
    }

    [Fact]
    public async Task The_review_list_can_be_filtered_by_status_and_pages()
    {
        var approved = await ApprovedAsync();
        await AppliedAsync(_otherDriver, Bicycle());

        var all = await _drivers.AdminListAsync(new DriverListQuery(null, null, null), None);
        var pending = await _drivers.AdminListAsync(new DriverListQuery(DriverApprovalStatus.Pending, null, null), None);
        var firstPage = await _drivers.AdminListAsync(new DriverListQuery(null, 1, 1), None);

        Assert.Equal(2, all.TotalCount);
        Assert.Equal(_otherDriver, Assert.Single(pending.Items).UserId);
        Assert.Single(firstPage.Items);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.NotNull(approved);
    }

    [Fact]
    public async Task Approving_works_once_and_is_recorded()
    {
        var applied = await AppliedAsync();

        var approved = await _drivers.ApproveAsync(_driver, applied.Version, _b.Administrator, None);
        var twice = await _drivers.ApproveAsync(_driver, applied.Version, _b.Administrator, None);

        Assert.Equal(DriverApprovalStatus.Approved, approved.Value!.ApprovalStatus);
        Assert.Equal("driver_invalid_transition", twice.Error!.Code);
        var entry = Assert.Single(_b.Db.AuditEntries, e => e.Action == AuditActions.DriverApproved);
        Assert.Equal(_b.Administrator, entry.ActorUserId);
        Assert.Equal(_driver.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task Rejecting_and_suspending_need_a_reason_and_keep_it()
    {
        var applied = await AppliedAsync();
        Assert.Contains("reason", (await _drivers.RejectAsync(_driver, applied.Version, "  ", _b.Administrator, None)).Error!.FieldErrors!.Keys);

        var rejected = await _drivers.RejectAsync(_driver, applied.Version, " Photo unreadable ", _b.Administrator, None);
        Assert.Equal(DriverApprovalStatus.Rejected, rejected.Value!.ApprovalStatus);
        Assert.Equal("Photo unreadable", rejected.Value.ReviewNote);

        var other = await ApprovedAsync(_otherDriver, Bicycle());
        Assert.Contains("reason", (await _drivers.SuspendAsync(_otherDriver, other.Version, null, _b.Administrator, None)).Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task A_review_step_that_does_not_fit_the_status_is_refused()
    {
        var pending = await AppliedAsync();

        var suspend = await _drivers.SuspendAsync(_driver, pending.Version, "x", _b.Administrator, None);
        var reinstate = await _drivers.ReinstateAsync(_driver, pending.Version, _b.Administrator, None);

        Assert.Equal("driver_invalid_transition", suspend.Error!.Code);
        Assert.Equal("driver_invalid_transition", reinstate.Error!.Code);
        Assert.Equal(DriverApprovalStatus.Pending, (await _drivers.GetMineAsync(_driver, None)).ApprovalStatus);
    }

    [Fact]
    public async Task A_reviewer_who_saw_an_older_version_is_told_it_changed()
    {
        var applied = await AppliedAsync();

        var stale = await _drivers.ApproveAsync(_driver, applied.Version + 999, _b.Administrator, None);

        Assert.Equal("driver_changed", stale.Error!.Code);
        Assert.Equal(DriverApprovalStatus.Pending, (await _drivers.GetMineAsync(_driver, None)).ApprovalStatus);
        Assert.Equal("driver_not_found", (await _drivers.ApproveAsync(Guid.NewGuid(), 0, _b.Administrator, None)).Error!.Code);
    }

    // --- Leaving ---

    [Fact]
    public async Task A_driver_on_duty_cannot_delete_their_account_until_they_go_off_duty()
    {
        var approved = await ApprovedAsync();
        var guard = new BookingsDeletionGuard(_b.Db);
        Assert.Empty(await guard.GetBlockersAsync(_driver, None));

        var on = (await _drivers.SetDutyAsync(_driver, true, approved.Version, None)).Value!;
        Assert.Equal([BookingsDeletionGuard.DriverOnDuty], await guard.GetBlockersAsync(_driver, None));

        await _drivers.SetDutyAsync(_driver, false, on.Version, None);
        Assert.Empty(await guard.GetBlockersAsync(_driver, None));
    }

    [Fact]
    public async Task Nothing_is_stored_about_where_a_driver_is()
    {
        // Location is collected only during an active job (step 8.7): the profile has no place for it at all.
        var columns = typeof(DriverProfile).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(columns, c => c.Contains("Latitude", StringComparison.OrdinalIgnoreCase)
            || c.Contains("Longitude", StringComparison.OrdinalIgnoreCase)
            || c.Contains("Location", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await _b.Db.DriverProfiles.Where(p => p.IsOnDuty).ToListAsync());
    }
}
