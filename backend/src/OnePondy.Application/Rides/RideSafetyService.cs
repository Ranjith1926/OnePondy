using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Rides;

namespace OnePondy.Application.Rides;

/// <summary>
/// Safety on rides (spec Phase 9: SOS, trip sharing, incident reporting). An SOS can be raised by the rider or the driver while the driver holds the ride; an
/// incident can be reported by either for a few weeks afterwards; both land in one queue that staff work (the support policy: administrators and support
/// agents), and every open SOS shows on the operations page as an <i>act now</i> alert. A rider can share a link that shows the trip to someone they trust.
/// Only what is needed is shown: opening a report is audited, and contact details are in it for staff who may have to call.
/// </summary>
public sealed class RideSafetyService(
    IOnePondyDbContext db,
    IUserAccountStore accounts,
    ISecretHasher hasher,
    AuditTrail audit,
    IOptions<RideOptions> options,
    TimeProvider time)
{
    // --- Riders and drivers ---

    /// <summary>Raises an SOS. Raising a second while the first is still open returns the first: staff already have it.</summary>
    public async Task<Result<SafetyReceiptView>> RaiseSosAsync(Guid userId, RideParty party, Guid rideId, RaiseSosInput input, CancellationToken cancellationToken)
    {
        var ride = await FindMineAsync(userId, party, rideId, cancellationToken);
        if (ride is null)
        {
            return RideSafetyErrors.NotOnRide;
        }

        if (!ride.IsActive)
        {
            return RideSafetyErrors.NotActive;
        }

        var existing = await db.RideSafetyEvents.AsNoTracking().FirstOrDefaultAsync(
            e => e.RideId == rideId && e.ReportedByUserId == userId && e.Kind == RideSafetyKind.Sos && e.Status != RideSafetyStatus.Resolved,
            cancellationToken);
        if (existing is not null)
        {
            return Receipt(existing);
        }

        var errors = new FieldErrors();
        if (input.Latitude is null != input.Longitude is null
            || (input.Latitude is { } lat && (!double.IsFinite(lat) || lat is < -90 or > 90))
            || (input.Longitude is { } lon && (!double.IsFinite(lon) || lon is < -180 or > 180)))
        {
            errors.Add("latitude", "Send both a latitude between -90 and 90 and a longitude between -180 and 180, or neither.");
            return errors.ToError();
        }

        var raised = RideSafetyEvent.RaiseSos(rideId, userId, party, input.Latitude, input.Longitude, time.GetUtcNow());
        db.RideSafetyEvents.Add(raised);
        audit.Record(userId, AuditActions.RideSosRaised, AuditEntityTypes.RideSafety, raised.Id, new { rideId, party });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // Pressed twice at the same moment: the database allows one open emergency per person per ride, and that one is already with staff.
            db.ClearTracked();
            var first = await db.RideSafetyEvents.AsNoTracking().FirstAsync(
                e => e.RideId == rideId && e.ReportedByUserId == userId && e.Kind == RideSafetyKind.Sos && e.Status != RideSafetyStatus.Resolved,
                cancellationToken);
            return Receipt(first);
        }

        return Receipt(raised);
    }

    /// <summary>Reports something that happened on a ride the person was on, during it or within thirty days after.</summary>
    public async Task<Result<SafetyReceiptView>> ReportIncidentAsync(Guid userId, RideParty party, Guid rideId, ReportIncidentInput input, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var details = errors.Required("details", input.Details, RideSafetyEvent.MaxDetailsLength);
        if (input.Category is not { } category || !Enum.IsDefined(category) || category == RideSafetyCategory.Emergency)
        {
            errors.Add("category", "Choose what happened.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var ride = await FindMineAsync(userId, party, rideId, cancellationToken);
        if (ride is null)
        {
            return RideSafetyErrors.NotOnRide;
        }

        // Nothing to report until two people were on the ride together.
        if (ride.DriverUserId is null || ride.AcceptedAt is null)
        {
            return RideSafetyErrors.NoDriverYet;
        }

        var now = time.GetUtcNow();
        if (!ride.IsOpen && now - (ride.CompletedAt ?? ride.CancelledAt ?? ride.UpdatedAt) > RideSafetyEvent.ReportWindow)
        {
            return RideSafetyErrors.WindowPassed;
        }

        var open = await db.RideSafetyEvents.AsNoTracking().CountAsync(
            e => e.ReportedByUserId == userId && e.Kind == RideSafetyKind.Incident && e.Status != RideSafetyStatus.Resolved,
            cancellationToken);
        if (open >= options.Value.MaxOpenIncidentsPerPerson)
        {
            return RideSafetyErrors.TooManyOpen;
        }

        var reported = RideSafetyEvent.ReportIncident(rideId, userId, party, input.Category!.Value, details, now);
        db.RideSafetyEvents.Add(reported);
        audit.Record(userId, AuditActions.RideIncidentReported, AuditEntityTypes.RideSafety, reported.Id, new { rideId, party, category = reported.Category });
        return await SaveAsync(reported, cancellationToken);
    }

    // --- Trip sharing ---

    /// <summary>Makes a link for someone the rider trusts, replacing any earlier one for this ride. The secret is returned once.</summary>
    public async Task<Result<ShareLinkView>> CreateShareAsync(Guid riderUserId, Guid rideId, CancellationToken cancellationToken)
    {
        var ride = await db.Rides.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rideId && r.RiderUserId == riderUserId, cancellationToken);
        if (ride is null)
        {
            return RideSafetyErrors.NotOnRide;
        }

        if (!ride.IsActive)
        {
            return RideSafetyErrors.ShareNotAvailable;
        }

        var now = time.GetUtcNow();
        foreach (var old in await db.RideShares.Where(s => s.RideId == rideId && s.RevokedAt == null).ToListAsync(cancellationToken))
        {
            old.Revoke(now);
        }

        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var share = RideShare.Create(rideId, hasher.Hash(secret), now);
        db.RideShares.Add(share);
        audit.Record(riderUserId, AuditActions.RideShareCreated, AuditEntityTypes.RideSafety, rideId, null);
        await db.SaveChangesAsync(cancellationToken);
        return new ShareLinkView(secret, share.ExpiresAt);
    }

    /// <summary>Turns the rider's link off. Always allowed, and fine when there is none.</summary>
    public async Task<Result<bool>> RevokeShareAsync(Guid riderUserId, Guid rideId, CancellationToken cancellationToken)
    {
        if (!await db.Rides.AsNoTracking().AnyAsync(r => r.Id == rideId && r.RiderUserId == riderUserId, cancellationToken))
        {
            return RideSafetyErrors.NotOnRide;
        }

        var now = time.GetUtcNow();
        foreach (var share in await db.RideShares.Where(s => s.RideId == rideId && s.RevokedAt == null).ToListAsync(cancellationToken))
        {
            share.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// What someone with a link sees. A link that is unknown, turned off or older than a day shows nothing (not even that it existed); a link for a
    /// ride that has ended says so and shows nothing else.
    /// </summary>
    public async Task<SharedRideView?> GetSharedAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return null;
        }

        var hash = hasher.Hash(token);
        var share = await db.RideShares.AsNoTracking().FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        var now = time.GetUtcNow();
        if (share is null || !share.IsLive(now))
        {
            return null;
        }

        var ride = await db.Rides.AsNoTracking().SingleOrDefaultAsync(r => r.Id == share.RideId, cancellationToken);
        if (ride is null)
        {
            return null;
        }

        if (!ride.IsActive)
        {
            return new SharedRideView(true, ride.Status, null, null, null, null, null, null, null, ride.UpdatedAt);
        }

        var driver = ride.DriverUserId is { } driverId
            ? await db.RideDriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == driverId, cancellationToken)
            : null;
        var rider = await accounts.GetProfileAsync(ride.RiderUserId, cancellationToken);
        var location = ride is { DriverLatitude: { } lat, DriverLongitude: { } lon, DriverLocationAt: { } at } ? new RideLocationView(lat, lon, at) : null;
        return new SharedRideView(
            false,
            ride.Status,
            FirstName(rider?.DisplayName),
            driver is null ? null : FirstName(driver.FullName),
            driver?.VehicleClass,
            driver?.VehicleNumber,
            ride.PickupLabel,
            ride.DestinationLabel,
            location,
            ride.UpdatedAt);
    }

    // --- Staff ---

    public async Task<PagedResult<SafetySummary>> ListAsync(SafetyListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var events = db.RideSafetyEvents.AsNoTracking().AsQueryable();
        events = query.View switch
        {
            "all" => events,
            "resolved" => events.Where(e => e.Status == RideSafetyStatus.Resolved),
            _ => events.Where(e => e.Status != RideSafetyStatus.Resolved),
        };

        var total = await events.CountAsync(cancellationToken);

        // An open emergency before anything else, then the oldest waiting first.
        var items = await events
            .OrderBy(e => e.Status == RideSafetyStatus.Resolved ? 1 : 0)
            .ThenBy(e => e.Kind == RideSafetyKind.Sos ? 0 : 1)
            .ThenBy(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<SafetySummary>(
            [.. items.Select(e => new SafetySummary(e.Id, e.RideId, e.Kind, e.Category, e.ReportedBy, e.Status, e.Details, e.CreatedAt, e.AcknowledgedAt))],
            page,
            pageSize,
            total);
    }

    public async Task<SafetyCounts> CountsAsync(CancellationToken cancellationToken)
    {
        var open = db.RideSafetyEvents.AsNoTracking().Where(e => e.Status != RideSafetyStatus.Resolved);
        return new SafetyCounts(
            await open.CountAsync(e => e.Kind == RideSafetyKind.Sos, cancellationToken),
            await open.CountAsync(e => e.Kind == RideSafetyKind.Incident, cancellationToken),
            await open.MinAsync(e => (DateTimeOffset?)e.CreatedAt, cancellationToken));
    }

    /// <summary>Opens a report, with both people on the ride and how to reach them. Each opening is recorded.</summary>
    public async Task<Result<SafetyDetail>> GetAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken)
    {
        var report = await db.RideSafetyEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (report is null)
        {
            return RideSafetyErrors.NotFound;
        }

        audit.Record(staffUserId, AuditActions.RideSafetyViewed, AuditEntityTypes.RideSafety, report.Id, new { report.RideId });
        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(report, cancellationToken);
    }

    public Task<Result<SafetyDetail>> AcknowledgeAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        StepAsync(id, version, staffUserId, AuditActions.RideSafetyAcknowledged, null, e => e.Acknowledge(staffUserId, time.GetUtcNow()), cancellationToken);

    public Task<Result<SafetyDetail>> ResolveAsync(Guid id, ResolveSafetyInput input, Guid staffUserId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var note = errors.Required("note", input.Note, RideSafetyEvent.MaxNoteLength);
        return errors.Any
            ? Task.FromResult(Result<SafetyDetail>.Failure(errors.ToError()))
            : StepAsync(id, input.Version, staffUserId, AuditActions.RideSafetyResolved, null, e => e.Resolve(staffUserId, note, time.GetUtcNow()), cancellationToken);
    }

    // --- Shared ---

    private async Task<Result<SafetyDetail>> StepAsync(
        Guid id,
        uint version,
        Guid staffUserId,
        string action,
        object? details,
        Action<RideSafetyEvent> apply,
        CancellationToken cancellationToken)
    {
        var report = await db.RideSafetyEvents.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (report is null)
        {
            return RideSafetyErrors.NotFound;
        }

        if (report.Version != version)
        {
            return RideSafetyErrors.Changed;
        }

        db.ExpectVersion(report, version);
        try
        {
            apply(report);
        }
        catch (InvalidOperationException exception)
        {
            db.ClearTracked();
            return RideSafetyErrors.InvalidTransition(exception.Message);
        }

        audit.Record(staffUserId, action, AuditEntityTypes.RideSafety, report.Id, details ?? new { report.RideId });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            db.ClearTracked();
            return RideSafetyErrors.Changed;
        }

        return await ToDetailAsync(report, cancellationToken);
    }

    /// <summary>The ride, if the person was on it in the stated role. Anyone else gets "not found", whatever the ride.</summary>
    private Task<Ride?> FindMineAsync(Guid userId, RideParty party, Guid rideId, CancellationToken cancellationToken) =>
        party == RideParty.Rider
            ? db.Rides.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rideId && r.RiderUserId == userId, cancellationToken)
            : db.Rides.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rideId && r.DriverUserId == userId, cancellationToken);

    private async Task<Result<SafetyReceiptView>> SaveAsync(RideSafetyEvent report, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        return Receipt(report);
    }

    private SafetyReceiptView Receipt(RideSafetyEvent report) =>
        new(report.Id, report.Kind, report.Status, report.CreatedAt, options.Value.EmergencyNumber);

    private async Task<SafetyDetail> ToDetailAsync(RideSafetyEvent report, CancellationToken cancellationToken)
    {
        var ride = await db.Rides.AsNoTracking().SingleAsync(r => r.Id == report.RideId, cancellationToken);
        var driver = ride.DriverUserId is { } driverId
            ? await db.RideDriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == driverId, cancellationToken)
            : null;
        var riderAccount = await accounts.GetProfileAsync(ride.RiderUserId, cancellationToken);
        var driverAccount = ride.DriverUserId is { } id ? await accounts.GetProfileAsync(id, cancellationToken) : null;
        return new SafetyDetail(
            report.Id,
            report.Version,
            report.Kind,
            report.Category,
            report.ReportedBy,
            report.Status,
            report.Details,
            report.Latitude,
            report.Longitude,
            report.CreatedAt,
            report.AcknowledgedAt,
            report.AcknowledgedByUserId,
            report.ResolvedAt,
            report.ResolutionNote,
            new SafetyRideSummary(
                ride.Id, ride.Status, ride.VehicleClass, ride.PickupLabel, ride.DestinationLabel, ride.Fare, ride.RequestedAt, ride.StartedAt, ride.CompletedAt, driver?.VehicleNumber),
            new SafetyPerson(ride.RiderUserId, riderAccount?.DisplayName, riderAccount?.PhoneNumber, riderAccount?.Email),
            ride.DriverUserId is { } driverUser
                ? new SafetyPerson(driverUser, driver?.FullName, driverAccount?.PhoneNumber, driverAccount?.Email)
                : null);
    }

    private static string FirstName(string? fullName) =>
        fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
