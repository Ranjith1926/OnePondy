namespace OnePondy.Domain.Rides;

public enum RideStatus
{
    /// <summary>Searching: waiting for an eligible driver nearby to accept. The first to accept gets it.</summary>
    Requested = 1,

    /// <summary>A driver is on the way to the pickup.</summary>
    Accepted = 2,

    /// <summary>The driver is at the pickup, waiting for the rider.</summary>
    Arrived = 3,

    InProgress = 4,

    Completed = 5,

    Cancelled = 6,

    /// <summary>No driver took the request in time.</summary>
    Expired = 7,
}

public enum RideCancelledBy
{
    Rider = 1,
    Driver = 2,
    System = 3,
}

/// <summary>A point a rider chose, with the name they chose it by (a landmark or an address). The coordinates are not shown to drivers until they accept.</summary>
public sealed record RidePlace(double Latitude, double Longitude, string Label);

/// <summary>
/// The fare the server worked out for one trip (spec Phase 9: "an authoritative fare quote from the backend"). It is kept, valid for a few minutes,
/// and a ride request must carry it, so the fare a rider was shown is the fare they are charged. A quote is used for at most one ride.
/// </summary>
public sealed class RideQuote
{
    public const int MaxLabelLength = 200;

    private RideQuote()
    {
    }

    public Guid Id { get; private set; }

    public Guid RiderUserId { get; private set; }

    public RideVehicleClass VehicleClass { get; private set; }

    public double PickupLatitude { get; private set; }

    public double PickupLongitude { get; private set; }

    public string PickupLabel { get; private set; } = string.Empty;

    public double DestinationLatitude { get; private set; }

    public double DestinationLongitude { get; private set; }

    public string DestinationLabel { get; private set; } = string.Empty;

    /// <summary>The estimated road distance in kilometres, to one decimal place.</summary>
    public decimal DistanceKm { get; private set; }

    /// <summary>What the rider pays the driver in cash at the end, in rupees.</summary>
    public decimal Fare { get; private set; }

    public string Currency { get; private set; } = "INR";

    /// <summary>The cancellation terms that applied when the quote was made; they are copied onto the ride.</summary>
    public string CancellationTerms { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsValidAt(DateTimeOffset now) => now < ExpiresAt;

    public RidePlace Pickup => new(PickupLatitude, PickupLongitude, PickupLabel);

    public RidePlace Destination => new(DestinationLatitude, DestinationLongitude, DestinationLabel);

    public static RideQuote Create(
        Guid riderUserId,
        RideVehicleClass vehicleClass,
        RidePlace pickup,
        RidePlace destination,
        decimal distanceKm,
        decimal fare,
        string cancellationTerms,
        DateTimeOffset now,
        TimeSpan validFor) => new()
        {
            Id = Guid.CreateVersion7(),
            RiderUserId = riderUserId,
            VehicleClass = vehicleClass,
            PickupLatitude = pickup.Latitude,
            PickupLongitude = pickup.Longitude,
            PickupLabel = pickup.Label.Trim(),
            DestinationLatitude = destination.Latitude,
            DestinationLongitude = destination.Longitude,
            DestinationLabel = destination.Label.Trim(),
            DistanceKm = distanceKm,
            Fare = fare,
            CancellationTerms = cancellationTerms,
            CreatedAt = now,
            ExpiresAt = now + validFor,
        };
}

/// <summary>
/// One ride (spec Phase 9). It starts when a rider requests it with a fare quote and ends when the trip is completed, cancelled, or no driver took it:
/// <code>
/// requested  → accepted (an eligible driver takes it) · cancelled (the rider) · expired (nobody came)
/// accepted   → arrived · requested (the driver gives it up) · cancelled (the rider)
/// arrived    → in_progress · cancelled (the rider, or the driver with a reason)
/// in_progress→ completed
/// </code>
/// Once the trip has started it can only be completed: an emergency is handled by the safety features, not by cancelling. The database repeats
/// these rules and refuses to give a ride to a second driver, so two drivers accepting at the same moment cannot both get it. The driver's position is
/// kept on the ride only while it is accepted, arrived or in progress, and is erased when the ride ends (the database refuses to keep it any longer).
/// </summary>
public sealed class Ride
{
    public const int MaxReasonLength = 500;
    public const int MaxNoteLength = 200;

    private Ride()
    {
    }

    public Guid Id { get; private set; }

    public Guid RiderUserId { get; private set; }

    public Guid QuoteId { get; private set; }

    public RideVehicleClass VehicleClass { get; private set; }

    public RideStatus Status { get; private set; }

    public double PickupLatitude { get; private set; }

    public double PickupLongitude { get; private set; }

    public string PickupLabel { get; private set; } = string.Empty;

    public double DestinationLatitude { get; private set; }

    public double DestinationLongitude { get; private set; }

    public string DestinationLabel { get; private set; } = string.Empty;

    /// <summary>What the rider told the driver about where to find them ("blue gate"). Shown to the driver only after they accept.</summary>
    public string? PickupNote { get; private set; }

    public decimal DistanceKm { get; private set; }

    /// <summary>The fare the rider was quoted. It never changes.</summary>
    public decimal Fare { get; private set; }

    public string Currency { get; private set; } = "INR";

    public string CancellationTerms { get; private set; } = string.Empty;

    /// <summary>The driver who has the ride. Empty while requested; kept after the trip as the record of who drove.</summary>
    public Guid? DriverUserId { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>When a request nobody has taken is given up on.</summary>
    public DateTimeOffset SearchEndsAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? ArrivedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public RideCancelledBy? CancelledBy { get; private set; }

    public string? CancelReason { get; private set; }

    /// <summary>The cash the driver confirms they took at the end; it must equal the fare.</summary>
    public decimal? CashCollected { get; private set; }

    public double? DriverLatitude { get; private set; }

    public double? DriverLongitude { get; private set; }

    public DateTimeOffset? DriverLocationAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    /// <summary>Whether a driver holds the ride right now.</summary>
    public bool IsActive => Status is RideStatus.Accepted or RideStatus.Arrived or RideStatus.InProgress;

    /// <summary>Whether the ride has not ended yet: searching or active.</summary>
    public bool IsOpen => Status == RideStatus.Requested || IsActive;

    public RidePlace Pickup => new(PickupLatitude, PickupLongitude, PickupLabel);

    public RidePlace Destination => new(DestinationLatitude, DestinationLongitude, DestinationLabel);

    public static Ride Request(RideQuote quote, string? pickupNote, DateTimeOffset now, TimeSpan searchFor)
    {
        if (!quote.IsValidAt(now))
        {
            throw new InvalidOperationException("That fare quote has expired.");
        }

        return new Ride
        {
            Id = Guid.CreateVersion7(),
            RiderUserId = quote.RiderUserId,
            QuoteId = quote.Id,
            VehicleClass = quote.VehicleClass,
            Status = RideStatus.Requested,
            PickupLatitude = quote.PickupLatitude,
            PickupLongitude = quote.PickupLongitude,
            PickupLabel = quote.PickupLabel,
            DestinationLatitude = quote.DestinationLatitude,
            DestinationLongitude = quote.DestinationLongitude,
            DestinationLabel = quote.DestinationLabel,
            PickupNote = string.IsNullOrWhiteSpace(pickupNote) ? null : pickupNote.Trim(),
            DistanceKm = quote.DistanceKm,
            Fare = quote.Fare,
            Currency = quote.Currency,
            CancellationTerms = quote.CancellationTerms,
            RequestedAt = now,
            SearchEndsAt = now + searchFor,
            UpdatedAt = now,
        };
    }

    public void Accept(Guid driverUserId, DateTimeOffset now)
    {
        Require(RideStatus.Requested);
        Status = RideStatus.Accepted;
        DriverUserId = driverUserId;
        AcceptedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The driver gives the ride up before arriving, and it goes back to the other drivers with a fresh search window.</summary>
    public void GiveUp(Guid driverUserId, DateTimeOffset now, TimeSpan searchFor)
    {
        RequireDriver(driverUserId, RideStatus.Accepted);
        Status = RideStatus.Requested;
        DriverUserId = null;
        AcceptedAt = null;
        SearchEndsAt = now + searchFor;
        ClearLocation();
        UpdatedAt = now;
    }

    public void Arrive(Guid driverUserId, DateTimeOffset now)
    {
        RequireDriver(driverUserId, RideStatus.Accepted);
        Status = RideStatus.Arrived;
        ArrivedAt = now;
        UpdatedAt = now;
    }

    public void Start(Guid driverUserId, DateTimeOffset now)
    {
        RequireDriver(driverUserId, RideStatus.Arrived);
        Status = RideStatus.InProgress;
        StartedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The driver confirms the exact cash taken. The fare was fixed by the quote, so anything else is refused.</summary>
    public void Complete(Guid driverUserId, decimal cashCollected, DateTimeOffset now)
    {
        RequireDriver(driverUserId, RideStatus.InProgress);
        if (cashCollected != Fare)
        {
            throw new ArgumentException($"The fare is ₹{Fare:0.00}: confirm exactly what you collected.", nameof(cashCollected));
        }

        Status = RideStatus.Completed;
        CompletedAt = now;
        CashCollected = cashCollected;
        ClearLocation();
        UpdatedAt = now;
    }

    /// <summary>The rider cancels, any time before the trip starts.</summary>
    public void CancelByRider(Guid riderUserId, string? reason, DateTimeOffset now)
    {
        if (RiderUserId != riderUserId)
        {
            throw new InvalidOperationException("This ride is not yours.");
        }

        if (Status is not (RideStatus.Requested or RideStatus.Accepted or RideStatus.Arrived))
        {
            throw new InvalidOperationException(Status == RideStatus.InProgress ? "The trip has started, so it can't be cancelled." : $"A ride that is {Status} cannot be cancelled.");
        }

        End(RideStatus.Cancelled, RideCancelledBy.Rider, reason, now);
    }

    /// <summary>A driver who is at the pickup and cannot go on (the rider did not come, for example) ends the ride with a reason.</summary>
    public void CancelByDriver(Guid driverUserId, string reason, DateTimeOffset now)
    {
        RequireDriver(driverUserId, RideStatus.Arrived);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        End(RideStatus.Cancelled, RideCancelledBy.Driver, reason, now);
    }

    /// <summary>Nobody took the request in time.</summary>
    public void Expire(DateTimeOffset now)
    {
        Require(RideStatus.Requested);
        End(RideStatus.Expired, RideCancelledBy.System, "No driver was available.", now);
    }

    /// <summary>An administrator or the system ends a ride that cannot go on (a suspended driver, a safety decision).</summary>
    public void CancelBySystem(string reason, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException($"A ride that is {Status} cannot be cancelled.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        End(RideStatus.Cancelled, RideCancelledBy.System, reason, now);
    }

    /// <summary>Keeps only the latest position, and only while the driver holds the ride.</summary>
    public void RecordDriverLocation(Guid driverUserId, double latitude, double longitude, DateTimeOffset now)
    {
        if (!IsActive || DriverUserId != driverUserId)
        {
            throw new InvalidOperationException("Location is only shared during your own trip.");
        }

        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "That is not a position on Earth.");
        }

        DriverLatitude = latitude;
        DriverLongitude = longitude;
        DriverLocationAt = now;
        UpdatedAt = now;
    }

    private void End(RideStatus status, RideCancelledBy by, string? reason, DateTimeOffset now)
    {
        Status = status;
        CancelledAt = now;
        CancelledBy = by;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ClearLocation();
        UpdatedAt = now;
    }

    private void ClearLocation()
    {
        DriverLatitude = null;
        DriverLongitude = null;
        DriverLocationAt = null;
    }

    private void Require(RideStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"This ride is {Status}, not {expected}.");
        }
    }

    private void RequireDriver(Guid driverUserId, RideStatus expected)
    {
        if (DriverUserId != driverUserId)
        {
            throw new InvalidOperationException("This ride is not assigned to you.");
        }

        Require(expected);
    }
}
