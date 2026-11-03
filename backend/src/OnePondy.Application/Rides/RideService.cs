using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Rides;

namespace OnePondy.Application.Rides;

/// <summary>
/// Rides (spec Phase 9): fare quotes, requests, dispatch and the trip. A rider asks for fares, picks one, and the request is offered to eligible drivers
/// on duty who are near the pickup; the first to accept gets it (two requests for the same ride collide on its row version, and the database refuses to give
/// a ride to a second driver or a second ride to a driver). Only <see cref="RideDriverProfile.IsEligible"/> drivers on duty, of the right vehicle class and
/// close enough, are offered or can accept a ride. What each side sees is deliberately small: see <see cref="RideOfferView"/> and <see cref="DriverRideView"/>.
/// </summary>
public sealed class RideService(
    IOnePondyDbContext db,
    IUserAccountStore accounts,
    IRideNotifier notifier,
    IOptions<RideOptions> options,
    TimeProvider time,
    ILogger<RideService> logger)
{
    // --- The rider ---

    /// <summary>Asks for fares: one stored quote for each offered vehicle class, each marked available or not.</summary>
    public async Task<Result<RideQuotesView>> QuoteAsync(Guid riderUserId, RideQuoteInput input, CancellationToken cancellationToken)
    {
        if (!await ModuleOnAsync(cancellationToken))
        {
            return RideErrors.ModuleOff;
        }

        var settings = options.Value;
        var errors = new FieldErrors();
        var pickup = Place(errors, "pickup", input.PickupLatitude, input.PickupLongitude, input.PickupLabel, settings);
        var destination = Place(errors, "destination", input.DestinationLatitude, input.DestinationLongitude, input.DestinationLabel, settings);
        if (errors.Any)
        {
            return errors.ToError();
        }

        var straight = RideFare.StraightLineKm(pickup!.Latitude, pickup.Longitude, destination!.Latitude, destination.Longitude);
        var roadKm = RideFare.RoadKm(straight, settings.RoadFactor);
        if ((double)roadKm < settings.MinTripKm)
        {
            errors.Add("destination", "That is too close to your pickup for a ride.");
        }
        else if ((double)roadKm > settings.MaxTripKm)
        {
            errors.Add("destination", $"Rides can be at most {settings.MaxTripKm:0} km.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var now = time.GetUtcNow();
        var today = RideClock.Today(now);
        var nearby = await NearbyDriversAsync(pickup, today, now, riderUserId, cancellationToken);
        var quotes = new List<RideQuoteView>();
        foreach (var vehicleClass in settings.EnabledClasses.Distinct())
        {
            var fare = RideFare.Fare(settings.Tariffs[vehicleClass], roadKm);
            var quote = RideQuote.Create(riderUserId, vehicleClass, pickup, destination, roadKm, fare, settings.CancellationTerms, now, settings.QuoteValidFor);
            db.RideQuotes.Add(quote);
            quotes.Add(new RideQuoteView(
                quote.Id, vehicleClass, roadKm, fare, quote.Currency, quote.ExpiresAt, quote.CancellationTerms, nearby.Contains(vehicleClass)));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RideQuotesView(pickup.Label, destination.Label, quotes);
    }

    /// <summary>Requests a ride with a quote the rider was shown. The fare is the quote's, whatever the tariff says now.</summary>
    public async Task<Result<RideView>> RequestAsync(Guid riderUserId, RequestRideInput input, CancellationToken cancellationToken)
    {
        if (!await ModuleOnAsync(cancellationToken))
        {
            return RideErrors.ModuleOff;
        }

        var errors = new FieldErrors();
        var note = errors.Optional("pickupNote", input.PickupNote, Ride.MaxNoteLength);
        if (input.QuoteId is not { } quoteId)
        {
            errors.Add("quoteId", "Choose a fare first.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var quote = await db.RideQuotes.AsNoTracking().SingleOrDefaultAsync(q => q.Id == input.QuoteId && q.RiderUserId == riderUserId, cancellationToken);
        if (quote is null)
        {
            return RideErrors.QuoteNotFound;
        }

        if (await db.Rides.AsNoTracking().AnyAsync(r => r.QuoteId == quote.Id, cancellationToken))
        {
            return RideErrors.QuoteUsed;
        }

        var now = time.GetUtcNow();
        var settings = options.Value;
        if (!quote.IsValidAt(now))
        {
            return RideErrors.QuoteExpired;
        }

        if (!settings.EnabledClasses.Contains(quote.VehicleClass))
        {
            return RideErrors.ClassUnavailable;
        }

        if (await db.Rides.AsNoTracking().AnyAsync(r => r.RiderUserId == riderUserId
                && (r.Status == RideStatus.Requested || r.Status == RideStatus.Accepted || r.Status == RideStatus.Arrived || r.Status == RideStatus.InProgress),
            cancellationToken))
        {
            return RideErrors.RiderBusy;
        }

        var nearby = await NearbyDriversAsync(quote.Pickup, RideClock.Today(now), now, riderUserId, cancellationToken);
        if (!nearby.Contains(quote.VehicleClass))
        {
            return RideErrors.NoDrivers;
        }

        var ride = Ride.Request(quote, note, now, settings.SearchWindow);
        db.Rides.Add(ride);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // Two requests at the same moment, or the same fare used twice: the database allows only one.
            db.ClearTracked();
            return RideErrors.RiderBusy;
        }

        await TellAsync(ride, cancellationToken);
        return await ToRiderViewAsync(ride, cancellationToken);
    }

    /// <summary>The rider's ride that has not ended, or the one that ended most recently within a few minutes so the app can show how it finished.</summary>
    public async Task<RideView?> GetCurrentAsync(Guid riderUserId, CancellationToken cancellationToken)
    {
        var ride = await db.Rides.AsNoTracking()
            .Where(r => r.RiderUserId == riderUserId)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (ride is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var finishedRecently = !ride.IsOpen && now - (ride.CompletedAt ?? ride.CancelledAt ?? ride.UpdatedAt) < TimeSpan.FromMinutes(10);
        return ride.IsOpen || finishedRecently ? await ToRiderViewAsync(ride, cancellationToken) : null;
    }

    public async Task<Result<RideView>> GetAsync(Guid riderUserId, Guid rideId, CancellationToken cancellationToken)
    {
        var ride = await db.Rides.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rideId && r.RiderUserId == riderUserId, cancellationToken);
        return ride is null ? RideErrors.NotFound : await ToRiderViewAsync(ride, cancellationToken);
    }

    public async Task<PagedResult<RideView>> ListMineAsync(Guid riderUserId, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var (pageNumber, size) = Paging.Normalize(page, pageSize);
        var rides = db.Rides.AsNoTracking().Where(r => r.RiderUserId == riderUserId);
        var total = await rides.CountAsync(cancellationToken);
        var items = await rides.OrderByDescending(r => r.RequestedAt).Skip((pageNumber - 1) * size).Take(size).ToListAsync(cancellationToken);
        var views = new List<RideView>();
        foreach (var ride in items)
        {
            views.Add(await ToRiderViewAsync(ride, cancellationToken));
        }

        return new PagedResult<RideView>(views, pageNumber, size, total);
    }

    /// <summary>The rider cancels, any time before the trip starts.</summary>
    public async Task<Result<RideView>> CancelAsync(Guid riderUserId, Guid rideId, string? reason, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Optional("reason", reason, Ride.MaxReasonLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        var ride = await db.Rides.SingleOrDefaultAsync(r => r.Id == rideId && r.RiderUserId == riderUserId, cancellationToken);
        if (ride is null)
        {
            return RideErrors.NotFound;
        }

        // A second tap on cancel is not an error.
        if (ride.Status == RideStatus.Cancelled && ride.CancelledBy == RideCancelledBy.Rider)
        {
            return await ToRiderViewAsync(ride, cancellationToken);
        }

        try
        {
            ride.CancelByRider(riderUserId, trimmed, time.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return RideErrors.InvalidTransition(exception.Message);
        }

        var saved = await SaveAsync(ride, cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        await TellAsync(ride, cancellationToken);
        return await ToRiderViewAsync(ride, cancellationToken);
    }

    // --- The driver ---

    public async Task<DriverRidesView> GetMineAsync(Guid driverUserId, CancellationToken cancellationToken)
    {
        var current = await db.Rides.AsNoTracking().SingleOrDefaultAsync(
            r => r.DriverUserId == driverUserId && (r.Status == RideStatus.Accepted || r.Status == RideStatus.Arrived || r.Status == RideStatus.InProgress),
            cancellationToken);
        var now = time.GetUtcNow();
        var profile = await db.RideDriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == driverUserId, cancellationToken);
        var available = profile is not null && profile.IsAvailableForRides(RideClock.Today(now));
        if (current is not null)
        {
            return new DriverRidesView(await ToDriverViewAsync(current, cancellationToken), [], available);
        }

        var position = await db.RideDriverPositions.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == driverUserId, cancellationToken);
        if (!available || profile is null || position is null || !position.IsFresh(now))
        {
            return new DriverRidesView(null, [], available);
        }

        return new DriverRidesView(null, await OffersAsync(profile, position, now, cancellationToken), true);
    }

    public async Task<Result<DriverRideView>> AcceptAsync(Guid driverUserId, Guid rideId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var profile = await db.RideDriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == driverUserId, cancellationToken);
        if (profile is null || !profile.IsAvailableForRides(RideClock.Today(now)))
        {
            return RideErrors.NotAvailable;
        }

        var ride = await db.Rides.SingleOrDefaultAsync(r => r.Id == rideId, cancellationToken);
        if (ride is null)
        {
            return RideErrors.NotFound;
        }

        // Nobody takes their own request: a ride needs two different people.
        if (ride.RiderUserId == driverUserId)
        {
            return RideErrors.OwnRide;
        }

        // A second tap on "accept" by the driver who already has it is not an error.
        if (ride.Status == RideStatus.Accepted && ride.DriverUserId == driverUserId)
        {
            return await ToDriverViewAsync(ride, cancellationToken);
        }

        if (ride.Status != RideStatus.Requested || ride.SearchEndsAt <= now)
        {
            return RideErrors.Taken;
        }

        // Only the right kind of vehicle, with a recent position close to the pickup, can take it: the same rule that decides who is offered it.
        var position = await db.RideDriverPositions.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == driverUserId, cancellationToken);
        if (profile.VehicleClass != ride.VehicleClass
            || position is null
            || !position.IsFresh(now)
            || DistanceToPickupKm(position, ride) > options.Value.OfferRadiusKm)
        {
            return RideErrors.TooFar;
        }

        if (await HoldsRideAsync(driverUserId, cancellationToken))
        {
            return RideErrors.DriverBusy;
        }

        ride.Accept(driverUserId, now);
        var saved = await SaveAsync(ride, cancellationToken);
        if (!saved.IsSuccess)
        {
            // Lost the race: another driver got this ride, or this driver accepted two at the same moment and the database allowed only one.
            return saved.Error == RideErrors.Changed && await HoldsRideAsync(driverUserId, cancellationToken) ? RideErrors.DriverBusy : RideErrors.Taken;
        }

        await TellAsync(ride, cancellationToken);
        return await ToDriverViewAsync(ride, cancellationToken);
    }

    /// <summary>The driver gives the ride up before arriving, and it goes back to the other drivers.</summary>
    public Task<Result<DriverRideView>> GiveUpAsync(Guid driverUserId, Guid rideId, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, rideId, ride => ride.GiveUp(driverUserId, time.GetUtcNow(), options.Value.SearchWindow), cancellationToken);

    public Task<Result<DriverRideView>> ArriveAsync(Guid driverUserId, Guid rideId, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, rideId, ride => ride.Arrive(driverUserId, time.GetUtcNow()), cancellationToken);

    public Task<Result<DriverRideView>> StartAsync(Guid driverUserId, Guid rideId, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, rideId, ride => ride.Start(driverUserId, time.GetUtcNow()), cancellationToken);

    /// <summary>The trip is over. The driver confirms the exact cash taken, which must be the quoted fare.</summary>
    public async Task<Result<DriverRideView>> CompleteAsync(Guid driverUserId, Guid rideId, decimal? cashCollected, CancellationToken cancellationToken)
    {
        var ride = await db.Rides.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rideId && r.DriverUserId == driverUserId, cancellationToken);
        if (ride is null)
        {
            return RideErrors.NotFound;
        }

        if (cashCollected != ride.Fare)
        {
            var errors = new FieldErrors();
            errors.Add("cashCollected", $"Confirm the exact cash you collected: ₹{ride.Fare:0.00}.");
            return errors.ToError();
        }

        return await StepAsync(driverUserId, rideId, r => r.Complete(driverUserId, cashCollected.Value, time.GetUtcNow()), cancellationToken);
    }

    /// <summary>A driver at the pickup who cannot go on ends the ride with a reason.</summary>
    public async Task<Result<DriverRideView>> CancelAsDriverAsync(Guid driverUserId, Guid rideId, string? reason, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, Ride.MaxReasonLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        return await StepAsync(driverUserId, rideId, ride => ride.CancelByDriver(driverUserId, trimmed, time.GetUtcNow()), cancellationToken);
    }

    /// <summary>Stores the driver's latest position on the ride, only while they hold it. It is what the rider sees; it is erased when the ride ends.</summary>
    public async Task<Result<bool>> RecordLocationAsync(Guid driverUserId, Guid rideId, double? latitude, double? longitude, CancellationToken cancellationToken)
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

        var ride = await db.Rides.SingleOrDefaultAsync(r => r.Id == rideId && r.DriverUserId == driverUserId, cancellationToken);
        if (ride is null)
        {
            return RideErrors.NotFound;
        }

        try
        {
            ride.RecordDriverLocation(driverUserId, latitude!.Value, longitude!.Value, time.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return RideErrors.InvalidTransition(exception.Message);
        }

        var saved = await SaveAsync(ride, cancellationToken);
        return saved.IsSuccess ? true : saved.Error;
    }

    // --- Shared ---

    /// <summary>One step on the driver's own ride, saved with its conflict handling, and the people on the ride told.</summary>
    private async Task<Result<DriverRideView>> StepAsync(Guid driverUserId, Guid rideId, Action<Ride> step, CancellationToken cancellationToken)
    {
        var ride = await db.Rides.SingleOrDefaultAsync(r => r.Id == rideId && r.DriverUserId == driverUserId, cancellationToken);
        if (ride is null)
        {
            return RideErrors.NotFound;
        }

        try
        {
            step(ride);
        }
        catch (InvalidOperationException exception)
        {
            db.ClearTracked();
            return RideErrors.InvalidTransition(exception.Message);
        }

        var saved = await SaveAsync(ride, cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        await TellAsync(ride, cancellationToken);
        return await ToDriverViewAsync(ride, cancellationToken);
    }

    private async Task<Result<bool>> SaveAsync(Ride ride, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // Someone else (another driver, the rider cancelling, the expiry job) changed it at the same moment; nothing of ours was saved.
            db.ClearTracked();
            return RideErrors.Changed;
        }

        return true;
    }

    /// <summary>Best effort: a failed push never fails the ride, because the apps also poll.</summary>
    private async Task TellAsync(Ride ride, CancellationToken cancellationToken)
    {
        try
        {
            await notifier.RideChangedAsync(ride.Id, ride.RiderUserId, ride.DriverUserId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not push the change to ride {RideId}", ride.Id);
        }
    }

    private Task<bool> ModuleOnAsync(CancellationToken cancellationToken) =>
        db.ModuleFlags.AsNoTracking().AnyAsync(f => f.Key == ModuleKeys.Rides && f.Status == ModuleStatus.Available, cancellationToken);

    private Task<bool> HoldsRideAsync(Guid driverUserId, CancellationToken cancellationToken) =>
        db.Rides.AsNoTracking().AnyAsync(
            r => r.DriverUserId == driverUserId && (r.Status == RideStatus.Accepted || r.Status == RideStatus.Arrived || r.Status == RideStatus.InProgress),
            cancellationToken);

    private static RidePlace? Place(FieldErrors errors, string name, double? latitude, double? longitude, string? label, RideOptions settings)
    {
        var trimmed = errors.Required($"{name}Label", label, RideQuote.MaxLabelLength);
        if (latitude is not { } lat || !double.IsFinite(lat) || lat is < -90 or > 90
            || longitude is not { } lon || !double.IsFinite(lon) || lon is < -180 or > 180)
        {
            errors.Add(name, "Choose a place on the map.");
            return null;
        }

        if (RideFare.StraightLineKm(lat, lon, settings.ServiceCentreLatitude, settings.ServiceCentreLongitude) > settings.ServiceRadiusKm)
        {
            errors.Add(name, "Rides are only available around Puducherry.");
            return null;
        }

        return trimmed.Length == 0 ? null : new RidePlace(lat, lon, trimmed);
    }

    private static double DistanceToPickupKm(RideDriverPosition position, Ride ride) =>
        RideFare.StraightLineKm(position.Latitude, position.Longitude, ride.PickupLatitude, ride.PickupLongitude);

    /// <summary>The vehicle classes for which an eligible driver is on duty, with a recent position, near a point.</summary>
    private async Task<HashSet<RideVehicleClass>> NearbyDriversAsync(RidePlace pickup, DateOnly today, DateTimeOffset now, Guid riderUserId, CancellationToken cancellationToken)
    {
        var radius = options.Value.OfferRadiusKm;
        var oldest = now - RideDriverProfile.PositionFreshFor;
        var candidates = await (
            from p in db.RideDriverProfiles.AsNoTracking()
            join position in db.RideDriverPositions.AsNoTracking() on p.UserId equals position.UserId
            where p.ApprovalStatus == OnePondy.Domain.Delivery.DriverApprovalStatus.Approved
                && p.UserId != riderUserId
                && p.IsOnDuty
                && p.InsuranceValidUntil >= today
                && position.RecordedAt >= oldest
            select new { p.UserId, p.VehicleClass, position.Latitude, position.Longitude })
            .ToListAsync(cancellationToken);
        var busy = await BusyDriversAsync(candidates.Select(c => c.UserId).ToList(), cancellationToken);
        return
        [
            .. candidates
                .Where(c => !busy.Contains(c.UserId)
                    && RideFare.StraightLineKm(c.Latitude, c.Longitude, pickup.Latitude, pickup.Longitude) <= radius)
                .Select(c => c.VehicleClass),
        ];
    }

    private async Task<HashSet<Guid>> BusyDriversAsync(List<Guid> driverIds, CancellationToken cancellationToken) =>
        [
            .. await db.Rides.AsNoTracking()
                .Where(r => r.DriverUserId != null && driverIds.Contains(r.DriverUserId.Value)
                    && (r.Status == RideStatus.Accepted || r.Status == RideStatus.Arrived || r.Status == RideStatus.InProgress))
                .Select(r => r.DriverUserId!.Value)
                .ToListAsync(cancellationToken),
        ];

    private async Task<List<RideOfferView>> OffersAsync(RideDriverProfile profile, RideDriverPosition position, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var open = await db.Rides.AsNoTracking()
            .Where(r => r.Status == RideStatus.Requested && r.VehicleClass == profile.VehicleClass && r.SearchEndsAt > now && r.RiderUserId != profile.UserId)
            .OrderBy(r => r.RequestedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        return
        [
            .. open
                .Select(r => (Ride: r, Distance: DistanceToPickupKm(position, r)))
                .Where(x => x.Distance <= settings.OfferRadiusKm)
                .Take(settings.MaxOffers)
                .Select(x => new RideOfferView(
                    x.Ride.Id,
                    x.Ride.VehicleClass,
                    x.Ride.PickupLabel,
                    Math.Round(x.Distance, 1),
                    x.Ride.DestinationLabel,
                    x.Ride.DistanceKm,
                    x.Ride.Fare,
                    x.Ride.Currency,
                    x.Ride.RequestedAt)),
        ];
    }

    private async Task<RideView> ToRiderViewAsync(Ride ride, CancellationToken cancellationToken)
    {
        RideDriverCard? driver = null;
        if (ride.DriverUserId is { } driverId)
        {
            var profile = await db.RideDriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == driverId, cancellationToken);
            if (profile is not null)
            {
                driver = new RideDriverCard(FirstName(profile.FullName), profile.VehicleClass, profile.VehicleNumber);
            }
        }

        var location = ride is { IsActive: true, DriverLatitude: { } lat, DriverLongitude: { } lon, DriverLocationAt: { } at }
            ? new RideLocationView(lat, lon, at)
            : null;
        return new RideView(
            ride.Id,
            ride.Status,
            ride.VehicleClass,
            new RidePlaceView(ride.PickupLatitude, ride.PickupLongitude, ride.PickupLabel),
            new RidePlaceView(ride.DestinationLatitude, ride.DestinationLongitude, ride.DestinationLabel),
            ride.PickupNote,
            ride.DistanceKm,
            ride.Fare,
            ride.Currency,
            ride.CancellationTerms,
            ride.RequestedAt,
            ride.SearchEndsAt,
            ride.AcceptedAt,
            ride.ArrivedAt,
            ride.StartedAt,
            ride.CompletedAt,
            ride.CancelledAt,
            ride.CancelledBy,
            ride.CancelReason,
            driver,
            location,
            ride.Version);
    }

    private async Task<DriverRideView> ToDriverViewAsync(Ride ride, CancellationToken cancellationToken)
    {
        var rider = await accounts.GetProfileAsync(ride.RiderUserId, cancellationToken);
        return new DriverRideView(
            ride.Id,
            ride.Status,
            ride.VehicleClass,
            FirstName(rider?.DisplayName) is { Length: > 0 } first ? first : "Rider",
            new RidePlaceView(ride.PickupLatitude, ride.PickupLongitude, ride.PickupLabel),
            new RidePlaceView(ride.DestinationLatitude, ride.DestinationLongitude, ride.DestinationLabel),
            ride.PickupNote,
            ride.DistanceKm,
            ride.Fare,
            ride.Currency,
            ride.AcceptedAt,
            ride.ArrivedAt,
            ride.StartedAt,
            ride.Version);
    }

    private static string FirstName(string? fullName) =>
        fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
}
