using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Delivery;

namespace OnePondy.Application.Delivery;

/// <summary>
/// Delivery drivers (spec Phase 8, step 8.6): an account applies, an administrator reviews, and an approved driver starts and ends
/// shifts. Being on duty is what makes a driver eligible for jobs (<see cref="DriverProfile.IsAvailableForJobs"/>); nothing in this
/// service records where a driver is, because location is collected only during an active job (step 8.7).
/// </summary>
public sealed partial class DriverService(IOnePondyDbContext db, IUserAccountStore accounts, AuditTrail audit, TimeProvider time)
{
    [GeneratedRegex("^[A-Z0-9]{4,12}$")]
    private static partial Regex PlatePattern();

    [GeneratedRegex("^[A-Z0-9]{8,20}$")]
    private static partial Regex LicencePattern();

    // --- The driver ---

    public async Task<DriverProfileView> GetMineAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.DriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return profile is null ? NotConfigured(userId) : ToView(profile);
    }

    /// <summary>Applies the first time, and edits after. An approved driver who changes anything goes back to review (and off duty).</summary>
    public async Task<Result<DriverProfileView>> SaveMineAsync(Guid userId, DriverInput input, CancellationToken cancellationToken)
    {
        var content = Validate(input);
        if (!content.IsSuccess)
        {
            return content.Error;
        }

        var licence = content.Value.LicenceNumber;
        if (licence is not null
            && await db.DriverProfiles.AsNoTracking().AnyAsync(p => p.LicenceNumber == licence && p.UserId != userId, cancellationToken))
        {
            return DriverErrors.LicenceInUse;
        }

        var now = time.GetUtcNow();
        var existing = await db.DriverProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (existing is null)
        {
            var created = DriverProfile.Apply(userId, content.Value, now);
            db.DriverProfiles.Add(created);
            audit.Record(userId, AuditActions.DriverApplied, AuditEntityTypes.Driver, userId, new { content.Value.VehicleType });
            return await SaveAsync(created, cancellationToken);
        }

        if (input.Version is null)
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["version"] = ["Required: send the version you loaded."] });
        }

        if (!existing.CanEdit)
        {
            return DriverErrors.Locked;
        }

        db.ExpectVersion(existing, input.Version.Value);
        var before = existing.ApprovalStatus;
        existing.Edit(content.Value, now);
        audit.Record(
            userId,
            AuditActions.DriverUpdated,
            AuditEntityTypes.Driver,
            userId,
            new { content.Value.VehicleType, sentBackToReview = before != existing.ApprovalStatus });
        return await SaveAsync(existing, cancellationToken);
    }

    /// <summary>Starts or ends a shift. Only an approved driver can start one; anyone can end one.</summary>
    public async Task<Result<DriverProfileView>> SetDutyAsync(Guid userId, bool onDuty, uint expectedVersion, CancellationToken cancellationToken)
    {
        var profile = await db.DriverProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return DriverErrors.NotApplied;
        }

        if (profile.Version != expectedVersion)
        {
            return DriverErrors.Changed;
        }

        if (onDuty && !profile.CanGoOnDuty)
        {
            return DriverErrors.NotApproved;
        }

        db.ExpectVersion(profile, expectedVersion);
        if (onDuty)
        {
            profile.GoOnDuty(time.GetUtcNow());
        }
        else
        {
            profile.GoOffDuty(time.GetUtcNow());
        }

        return await SaveAsync(profile, cancellationToken);
    }

    /// <summary>Whether a driver may be offered jobs right now (approved and on duty). Nothing else makes a driver eligible.</summary>
    public Task<bool> IsAvailableForJobsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.DriverProfiles.AsNoTracking().AnyAsync(
            p => p.UserId == userId && p.ApprovalStatus == DriverApprovalStatus.Approved && p.IsOnDuty, cancellationToken);

    // --- Administrators ---

    public async Task<PagedResult<DriverSummary>> AdminListAsync(DriverListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var drivers = db.DriverProfiles.AsNoTracking();
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
        return new PagedResult<DriverSummary>(
            [.. items.Select(d => new DriverSummary(d.UserId, d.FullName, d.VehicleType, d.ApprovalStatus, d.IsOnDuty, d.CreatedAt, d.UpdatedAt))],
            page,
            pageSize,
            total);
    }

    public async Task<Result<DriverDetail>> AdminGetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.DriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return profile is null ? DriverErrors.NotFound : await ToDetailAsync(profile, cancellationToken);
    }

    public Task<Result<DriverDetail>> ApproveAsync(Guid userId, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        ReviewAsync(userId, version, staffUserId, AuditActions.DriverApproved, null, p => p.Approve(time.GetUtcNow()), cancellationToken);

    public Task<Result<DriverDetail>> RejectAsync(Guid userId, uint version, string? reason, Guid staffUserId, CancellationToken cancellationToken) =>
        WithReason(reason, trimmed => ReviewAsync(
            userId, version, staffUserId, AuditActions.DriverRejected, new { reason = trimmed }, p => p.Reject(trimmed, time.GetUtcNow()), cancellationToken));

    public Task<Result<DriverDetail>> SuspendAsync(Guid userId, uint version, string? reason, Guid staffUserId, CancellationToken cancellationToken) =>
        WithReason(reason, trimmed => ReviewAsync(
            userId, version, staffUserId, AuditActions.DriverSuspended, new { reason = trimmed }, p => p.Suspend(trimmed, time.GetUtcNow()), cancellationToken));

    public Task<Result<DriverDetail>> ReinstateAsync(Guid userId, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        ReviewAsync(userId, version, staffUserId, AuditActions.DriverReinstated, null, p => p.Reinstate(time.GetUtcNow()), cancellationToken);

    // --- Shared ---

    private static Task<Result<DriverDetail>> WithReason(string? reason, Func<string, Task<Result<DriverDetail>>> then)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, DriverProfile.MaxReasonLength);
        return errors.Any ? Task.FromResult(Result<DriverDetail>.Failure(errors.ToError())) : then(trimmed);
    }

    /// <summary>One review step. The reviewer acts on the version they saw, never on details changed since.</summary>
    private async Task<Result<DriverDetail>> ReviewAsync(
        Guid userId,
        uint version,
        Guid staffUserId,
        string auditAction,
        object? auditDetails,
        Action<DriverProfile> apply,
        CancellationToken cancellationToken)
    {
        var profile = await db.DriverProfiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return DriverErrors.NotFound;
        }

        if (profile.Version != version)
        {
            return DriverErrors.Changed;
        }

        db.ExpectVersion(profile, version);
        try
        {
            apply(profile);
        }
        catch (InvalidOperationException exception)
        {
            return DriverErrors.InvalidTransition(exception.Message);
        }

        audit.Record(staffUserId, auditAction, AuditEntityTypes.Driver, userId, auditDetails);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DriverErrors.Changed;
        }

        return await ToDetailAsync(profile, cancellationToken);
    }

    private async Task<Result<DriverProfileView>> SaveAsync(DriverProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DriverErrors.Changed;
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // Two applications at once, or the same licence registered on two accounts at once: the database allows only one.
            db.ClearTracked();
            return DriverErrors.Changed;
        }

        return ToView(profile);
    }

    private static Result<DriverContent> Validate(DriverInput input)
    {
        var errors = new FieldErrors();
        var name = errors.Required("fullName", input.FullName, DriverProfile.MaxNameLength);
        if (name.Length > 0 && (name.Length < DriverProfile.MinNameLength || name.Any(char.IsControl)))
        {
            errors.Add("fullName", $"Enter your full name, at least {DriverProfile.MinNameLength} characters.");
        }

        if (input.VehicleType is not { } vehicle)
        {
            errors.Add("vehicleType", "Choose how you will deliver.");
            return errors.ToError();
        }

        string? plate = null;
        string? licence = null;
        if (DriverProfile.NeedsDocuments(vehicle))
        {
            plate = Normalised(input.VehicleNumber);
            if (plate is null || !PlatePattern().IsMatch(plate))
            {
                errors.Add("vehicleNumber", "Enter the registration number on your vehicle's number plate.");
            }

            licence = Normalised(input.LicenceNumber);
            if (licence is null || !LicencePattern().IsMatch(licence))
            {
                errors.Add("licenceNumber", "Enter your driving licence number.");
            }
        }

        return errors.Any ? errors.ToError() : new DriverContent(name, vehicle, plate, licence);
    }

    private static string? Normalised(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : DriverProfile.Normalise(value);

    private static DriverProfileView NotConfigured(Guid userId) =>
        new(false, userId, null, null, null, null, null, null, false, null, false, 0);

    private static DriverProfileView ToView(DriverProfile profile) => new(
        true,
        profile.UserId,
        profile.FullName,
        profile.VehicleType,
        profile.VehicleNumber,
        profile.LicenceNumber,
        profile.ApprovalStatus,
        profile.ReviewNote,
        profile.IsOnDuty,
        profile.OnDutySince,
        profile.CanGoOnDuty,
        profile.Version);

    private async Task<DriverDetail> ToDetailAsync(DriverProfile profile, CancellationToken cancellationToken)
    {
        var account = await accounts.GetProfileAsync(profile.UserId, cancellationToken);
        return new DriverDetail(
            profile.UserId,
            profile.Version,
            profile.FullName,
            profile.VehicleType,
            profile.VehicleNumber,
            profile.LicenceNumber,
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
