using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Rides;

namespace OnePondy.Application.Rides;

/// <summary>What day it is where OnePondy operates. A policy that "ends on" a date covers that whole day in India, not in UTC.</summary>
public static class RideClock
{
    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(India).DateTime);
}

/// <summary>
/// Drivers in ride mode (spec Phase 9): an account applies with its vehicle, licence, permit and insurance; an administrator reviews; an approved,
/// insured driver starts and ends shifts and, while on duty, shares their position so jobs can be offered to the nearest eligible drivers.
/// <see cref="RideDriverProfile.IsEligible"/> is the one definition of "may receive ride jobs"; this service and dispatch both use it.
/// </summary>
public sealed partial class RideDriverService(
    IOnePondyDbContext db,
    IUserAccountStore accounts,
    AuditTrail audit,
    IOptions<RideOptions> options,
    TimeProvider time)
{
    [GeneratedRegex("^[A-Z0-9]{4,12}$")]
    private static partial Regex PlatePattern();

    [GeneratedRegex("^[A-Z0-9]{8,20}$")]
    private static partial Regex LicencePattern();

    [GeneratedRegex("^[A-Z0-9]{4,30}$")]
    private static partial Regex PermitPattern();

    [GeneratedRegex("^[A-Z0-9]{4,30}$")]
    private static partial Regex PolicyPattern();

    // --- The driver ---

    public async Task<RideDriverView> GetMineAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.RideDriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return profile is null ? NotConfigured(userId) : ToView(profile);
    }

    /// <summary>Applies the first time, and edits after. An approved driver who changes anything goes back to review (and off duty).</summary>
    public async Task<Result<RideDriverView>> SaveMineAsync(Guid userId, RideDriverInput input, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var content = Validate(input, RideClock.Today(now));
        if (!content.IsSuccess)
        {
            return content.Error;
        }

        var licence = content.Value.LicenceNumber;
        var vehicle = content.Value.VehicleNumber;
        if (await db.RideDriverProfiles.AsNoTracking().AnyAsync(
                p => p.UserId != userId && (p.LicenceNumber == licence || p.VehicleNumber == vehicle), cancellationToken))
        {
            return RideDriverErrors.DocumentInUse;
        }

        var existing = await db.RideDriverProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (existing is null)
        {
            var created = RideDriverProfile.Apply(userId, content.Value, now);
            db.RideDriverProfiles.Add(created);
            audit.Record(userId, AuditActions.RideDriverApplied, AuditEntityTypes.RideDriver, userId, new { content.Value.VehicleClass });
            return await SaveAsync(created, cancellationToken);
        }

        if (input.Version is null)
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["version"] = ["Required: send the version you loaded."] });
        }

        if (!existing.CanEdit)
        {
            return RideDriverErrors.Locked;
        }

        // Changing details sends an approved driver back for review and off duty, which cannot happen in the middle of a trip.
        if (await HoldsRideAsync(userId, cancellationToken))
        {
            return RideDriverErrors.HasActiveRide;
        }

        db.ExpectVersion(existing, input.Version.Value);
        var before = existing.ApprovalStatus;
        existing.Edit(content.Value, now);
        audit.Record(
            userId,
            AuditActions.RideDriverUpdated,
            AuditEntityTypes.RideDriver,
            userId,
            new { content.Value.VehicleClass, sentBackToReview = before != existing.ApprovalStatus });
        return await SaveAsync(existing, cancellationToken);
    }

    /// <summary>Starts or ends a shift. Only an approved, insured driver can start one; anyone can end one.</summary>
    public async Task<Result<RideDriverView>> SetDutyAsync(Guid userId, bool onDuty, uint expectedVersion, CancellationToken cancellationToken)
    {
        var profile = await db.RideDriverProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return RideDriverErrors.NotApplied;
        }

        if (profile.Version != expectedVersion)
        {
            return RideDriverErrors.Changed;
        }

        var now = time.GetUtcNow();
        var today = RideClock.Today(now);
        if (onDuty && !profile.IsEligible(today))
        {
            return profile.ApprovalStatus == DriverApprovalStatus.Approved ? RideDriverErrors.InsuranceEnded : RideDriverErrors.NotApproved;
        }

        // A driver with a trip under way finishes it before going off duty.
        if (!onDuty && await HoldsRideAsync(userId, cancellationToken))
        {
            return RideDriverErrors.HasActiveRide;
        }

        db.ExpectVersion(profile, expectedVersion);
        if (onDuty)
        {
            profile.GoOnDuty(today, now);
        }
        else
        {
            profile.GoOffDuty(now);
        }

        return await SaveAsync(profile, cancellationToken);
    }

    /// <summary>
    /// Stores the driver's latest position for dispatch, only while on duty. Nothing is kept beyond the latest, and going off duty erases it.
    /// A driver whose insurance has ended since they started the shift is taken off duty rather than kept on the map.
    /// </summary>
    public async Task<Result<bool>> ShareLocationAsync(Guid userId, double? latitude, double? longitude, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        if (latitude is not { } lat || !double.IsFinite(lat) || lat is < -90 or > 90)
        {
            errors.Add("latitude", "Latitude must be between -90 and 90.");
        }

        if (longitude is not { } lon || !double.IsFinite(lon) || lon is < -180 or > 180)
        {
            errors.Add("longitude", "Longitude must be between -180 and 180.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var profile = await db.RideDriverProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return RideDriverErrors.NotApplied;
        }

        var now = time.GetUtcNow();
        if (!profile.IsOnDuty)
        {
            return RideDriverErrors.NotOnDuty;
        }

        if (!profile.IsEligible(RideClock.Today(now)) && !await HoldsRideAsync(userId, cancellationToken))
        {
            profile.GoOffDuty(now);
            await db.SaveChangesAsync(cancellationToken);
            return RideDriverErrors.InsuranceEnded;
        }

        profile.ShareLocation(latitude!.Value, longitude!.Value, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // The driver went off duty (or was suspended) at the same moment; the position is simply not kept.
            db.ClearTracked();
            return RideDriverErrors.NotOnDuty;
        }

        return true;
    }

    private Task<bool> HoldsRideAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Rides.AsNoTracking().AnyAsync(
            r => r.DriverUserId == userId && (r.Status == RideStatus.Accepted || r.Status == RideStatus.Arrived || r.Status == RideStatus.InProgress),
            cancellationToken);

    // --- Administrators ---

    public async Task<PagedResult<RideDriverSummary>> AdminListAsync(RideDriverListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var today = RideClock.Today(time.GetUtcNow());
        var drivers = db.RideDriverProfiles.AsNoTracking();
        if (query.Status is { } status)
        {
            drivers = drivers.Where(d => d.ApprovalStatus == status);
        }

        var total = await drivers.CountAsync(cancellationToken);
        var items = await drivers
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<RideDriverSummary>(
            [.. items.Select(d => new RideDriverSummary(
                d.UserId, d.FullName, d.VehicleClass, d.ApprovalStatus, d.IsOnDuty, d.InsuranceValidUntil, d.InsuranceValidUntil < today, d.CreatedAt, d.UpdatedAt))],
            page,
            pageSize,
            total);
    }

    public async Task<Result<RideDriverDetail>> AdminGetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.RideDriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return profile is null ? RideDriverErrors.NotFound : await ToDetailAsync(profile, cancellationToken);
    }

    public Task<Result<RideDriverDetail>> ApproveAsync(Guid userId, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        ReviewAsync(userId, version, staffUserId, AuditActions.RideDriverApproved, null, p => p.Approve(time.GetUtcNow()), cancellationToken);

    public Task<Result<RideDriverDetail>> RejectAsync(Guid userId, uint version, string? reason, Guid staffUserId, CancellationToken cancellationToken) =>
        WithReason(reason, trimmed => ReviewAsync(
            userId, version, staffUserId, AuditActions.RideDriverRejected, new { reason = trimmed }, p => p.Reject(trimmed, time.GetUtcNow()), cancellationToken));

    public Task<Result<RideDriverDetail>> SuspendAsync(Guid userId, uint version, string? reason, Guid staffUserId, CancellationToken cancellationToken) =>
        WithReason(reason, trimmed => ReviewAsync(
            userId, version, staffUserId, AuditActions.RideDriverSuspended, new { reason = trimmed }, p => p.Suspend(trimmed, time.GetUtcNow()), cancellationToken));

    public Task<Result<RideDriverDetail>> ReinstateAsync(Guid userId, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        ReviewAsync(userId, version, staffUserId, AuditActions.RideDriverReinstated, null, p => p.Reinstate(time.GetUtcNow()), cancellationToken);

    // --- Shared ---

    private static Task<Result<RideDriverDetail>> WithReason(string? reason, Func<string, Task<Result<RideDriverDetail>>> then)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, RideDriverProfile.MaxReasonLength);
        return errors.Any ? Task.FromResult(Result<RideDriverDetail>.Failure(errors.ToError())) : then(trimmed);
    }

    /// <summary>One review step. The reviewer acts on the version they saw, never on details changed since.</summary>
    private async Task<Result<RideDriverDetail>> ReviewAsync(
        Guid userId,
        uint version,
        Guid staffUserId,
        string auditAction,
        object? auditDetails,
        Action<RideDriverProfile> apply,
        CancellationToken cancellationToken)
    {
        var profile = await db.RideDriverProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return RideDriverErrors.NotFound;
        }

        if (profile.Version != version)
        {
            return RideDriverErrors.Changed;
        }

        db.ExpectVersion(profile, version);
        try
        {
            apply(profile);
        }
        catch (InvalidOperationException exception)
        {
            return RideDriverErrors.InvalidTransition(exception.Message);
        }

        // A suspended driver's ride that they have not reached yet goes back to the other drivers, in the same save. One where they have already
        // arrived or the trip has started stays with them: the rider is with them, and an administrator decides what happens to it.
        if (profile.ApprovalStatus == DriverApprovalStatus.Suspended)
        {
            var held = await db.Rides.Where(r => r.DriverUserId == userId && r.Status == RideStatus.Accepted).ToListAsync(cancellationToken);
            foreach (var ride in held)
            {
                ride.GiveUp(userId, time.GetUtcNow(), options.Value.SearchWindow);
            }
        }

        audit.Record(staffUserId, auditAction, AuditEntityTypes.RideDriver, userId, auditDetails);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RideDriverErrors.Changed;
        }

        return await ToDetailAsync(profile, cancellationToken);
    }

    private async Task<Result<RideDriverView>> SaveAsync(RideDriverProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RideDriverErrors.Changed;
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // Two applications at once, or the same licence or vehicle registered on two accounts at once: the database allows only one.
            db.ClearTracked();
            return RideDriverErrors.Changed;
        }

        return ToView(profile);
    }

    private static Result<RideDriverContent> Validate(RideDriverInput input, DateOnly today)
    {
        var errors = new FieldErrors();
        var name = errors.Required("fullName", input.FullName, RideDriverProfile.MaxNameLength);
        if (name.Length > 0 && (name.Length < RideDriverProfile.MinNameLength || name.Any(char.IsControl)))
        {
            errors.Add("fullName", $"Enter your full name, at least {RideDriverProfile.MinNameLength} characters.");
        }

        if (input.VehicleClass is not { } vehicleClass || !Enum.IsDefined(vehicleClass))
        {
            errors.Add("vehicleClass", "Choose the kind of vehicle you will drive.");
        }

        var plate = Normalised(input.VehicleNumber);
        if (plate is null || !PlatePattern().IsMatch(plate))
        {
            errors.Add("vehicleNumber", "Enter the registration number on your vehicle's number plate.");
        }

        var licence = Normalised(input.LicenceNumber);
        if (licence is null || !LicencePattern().IsMatch(licence))
        {
            errors.Add("licenceNumber", "Enter your driving licence number.");
        }

        var permit = Normalised(input.PermitNumber);
        if (permit is null || !PermitPattern().IsMatch(permit))
        {
            errors.Add("permitNumber", "Enter the number of your permit to carry passengers.");
        }

        var policy = Normalised(input.InsurancePolicyNumber);
        if (policy is null || !PolicyPattern().IsMatch(policy))
        {
            errors.Add("insurancePolicyNumber", "Enter your insurance policy number.");
        }

        if (input.InsuranceValidUntil is not { } until)
        {
            errors.Add("insuranceValidUntil", "Enter the date your insurance ends.");
        }
        else if (until < today)
        {
            errors.Add("insuranceValidUntil", "Your insurance has already ended. Renew it first.");
        }

        return errors.Any
            ? errors.ToError()
            : new RideDriverContent(name, input.VehicleClass!.Value, plate!, licence!, permit!, policy!, input.InsuranceValidUntil!.Value);
    }

    private static string? Normalised(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : RideDriverProfile.Normalise(value);

    private static RideDriverView NotConfigured(Guid userId) =>
        new(false, userId, null, null, null, null, null, null, null, null, null, false, null, false, false, 0);

    private RideDriverView ToView(RideDriverProfile profile)
    {
        var today = RideClock.Today(time.GetUtcNow());
        return new RideDriverView(
            true,
            profile.UserId,
            profile.FullName,
            profile.VehicleClass,
            profile.VehicleNumber,
            profile.LicenceNumber,
            profile.PermitNumber,
            profile.InsurancePolicyNumber,
            profile.InsuranceValidUntil,
            profile.ApprovalStatus,
            profile.ReviewNote,
            profile.IsOnDuty,
            profile.OnDutySince,
            profile.IsEligible(today),
            profile.InsuranceValidUntil < today,
            profile.Version);
    }

    private async Task<RideDriverDetail> ToDetailAsync(RideDriverProfile profile, CancellationToken cancellationToken)
    {
        var account = await accounts.GetProfileAsync(profile.UserId, cancellationToken);
        return new RideDriverDetail(
            profile.UserId,
            profile.Version,
            profile.FullName,
            profile.VehicleClass,
            profile.VehicleNumber,
            profile.LicenceNumber,
            profile.PermitNumber,
            profile.InsurancePolicyNumber,
            profile.InsuranceValidUntil,
            profile.InsuranceValidUntil < RideClock.Today(time.GetUtcNow()),
            profile.ApprovalStatus,
            profile.ReviewNote,
            profile.IsOnDuty,
            profile.OnDutySince,
            account?.PhoneNumber,
            account?.Email,
            profile.CreatedAt,
            profile.UpdatedAt);
    }
}
