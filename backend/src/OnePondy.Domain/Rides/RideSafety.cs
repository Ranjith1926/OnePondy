namespace OnePondy.Domain.Rides;

public enum RideSafetyKind
{
    /// <summary>Someone pressed the emergency button during a ride. It needs a person to look at it now.</summary>
    Sos = 1,

    /// <summary>A report about something that happened on a ride, made during or after it.</summary>
    Incident = 2,
}

public enum RideSafetyCategory
{
    /// <summary>Always the category of an SOS.</summary>
    Emergency = 1,

    UnsafeDriving = 2,
    Harassment = 3,
    Accident = 4,
    VehicleProblem = 5,
    Overcharge = 6,
    Other = 7,
}

public enum RideSafetyStatus
{
    /// <summary>Nobody has looked at it yet.</summary>
    Open = 1,

    /// <summary>A member of staff has seen it and is dealing with it.</summary>
    Acknowledged = 2,

    Resolved = 3,
}

/// <summary>Who on the ride made the report.</summary>
public enum RideParty
{
    Rider = 1,
    Driver = 2,
}

/// <summary>
/// A safety report on a ride (spec Phase 9: SOS, incident reporting). An SOS can only be raised while a driver holds the ride, carries where the person
/// was, and must be looked at by staff at once: the operations page shows every open one as an <i>act now</i> alert. An incident is a report about a ride
/// the person was on, during or for a few weeks after, and goes to the same queue. Staff take it (acknowledge) and close it with a note; the person who
/// reported it is never told anything that identifies the other party beyond what the ride already shows them.
/// <code>
/// open → acknowledged → resolved
/// open → resolved
/// </code>
/// </summary>
public sealed class RideSafetyEvent
{
    public const int MaxDetailsLength = 1000;
    public const int MaxNoteLength = 1000;

    /// <summary>How long after a ride ended an incident can still be reported.</summary>
    public static readonly TimeSpan ReportWindow = TimeSpan.FromDays(30);

    private RideSafetyEvent()
    {
    }

    public Guid Id { get; private set; }

    public Guid RideId { get; private set; }

    public RideSafetyKind Kind { get; private set; }

    public RideSafetyCategory Category { get; private set; }

    public Guid ReportedByUserId { get; private set; }

    public RideParty ReportedBy { get; private set; }

    /// <summary>What the person wrote (an incident), or null (an SOS usually has no words).</summary>
    public string? Details { get; private set; }

    /// <summary>Where the person was when they raised an SOS, if their phone told us. Never set for an incident.</summary>
    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public RideSafetyStatus Status { get; private set; }

    public Guid? AcknowledgedByUserId { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>What staff did about it, written when they close it.</summary>
    public string? ResolutionNote { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public bool IsClosed => Status == RideSafetyStatus.Resolved;

    public static RideSafetyEvent RaiseSos(Guid rideId, Guid userId, RideParty party, double? latitude, double? longitude, DateTimeOffset now)
    {
        if (latitude is null != longitude is null
            || (latitude is { } lat && (!double.IsFinite(lat) || lat is < -90 or > 90))
            || (longitude is { } lon && (!double.IsFinite(lon) || lon is < -180 or > 180)))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "That is not a position on Earth.");
        }

        return new RideSafetyEvent
        {
            Id = Guid.CreateVersion7(),
            RideId = rideId,
            Kind = RideSafetyKind.Sos,
            Category = RideSafetyCategory.Emergency,
            ReportedByUserId = userId,
            ReportedBy = party,
            Latitude = latitude,
            Longitude = longitude,
            Status = RideSafetyStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static RideSafetyEvent ReportIncident(Guid rideId, Guid userId, RideParty party, RideSafetyCategory category, string details, DateTimeOffset now)
    {
        if (category == RideSafetyCategory.Emergency)
        {
            throw new ArgumentException("An emergency is raised with the emergency button, not reported.", nameof(category));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(details);
        return new RideSafetyEvent
        {
            Id = Guid.CreateVersion7(),
            RideId = rideId,
            Kind = RideSafetyKind.Incident,
            Category = category,
            ReportedByUserId = userId,
            ReportedBy = party,
            Details = details.Trim(),
            Status = RideSafetyStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>A member of staff has seen it. Taking it again is fine and changes nothing.</summary>
    public void Acknowledge(Guid staffUserId, DateTimeOffset now)
    {
        if (Status == RideSafetyStatus.Resolved)
        {
            throw new InvalidOperationException("This report is already resolved.");
        }

        if (Status == RideSafetyStatus.Acknowledged)
        {
            return;
        }

        Status = RideSafetyStatus.Acknowledged;
        AcknowledgedByUserId = staffUserId;
        AcknowledgedAt = now;
        UpdatedAt = now;
    }

    public void Resolve(Guid staffUserId, string note, DateTimeOffset now)
    {
        if (Status == RideSafetyStatus.Resolved)
        {
            throw new InvalidOperationException("This report is already resolved.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        Status = RideSafetyStatus.Resolved;
        ResolvedByUserId = staffUserId;
        ResolvedAt = now;
        ResolutionNote = note.Trim();
        UpdatedAt = now;
    }
}

/// <summary>
/// A link a rider shares so someone they trust can follow a trip (spec Phase 9: trip sharing). The link carries a secret that is stored only as a hash,
/// shows the ride's status, the driver's first name and vehicle and, while the ride is active, the driver's position, and shows nothing at all once the
/// ride has ended, been revoked, or a day has passed. A rider has one link per ride: making another replaces the first.
/// </summary>
public sealed class RideShare
{
    /// <summary>A link is never valid for longer than this, however long the ride runs.</summary>
    public static readonly TimeSpan ValidFor = TimeSpan.FromHours(24);

    private RideShare()
    {
    }

    public Guid Id { get; private set; }

    public Guid RideId { get; private set; }

    /// <summary>The hash of the secret in the link. The secret itself is never stored.</summary>
    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsLive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public static RideShare Create(Guid rideId, byte[] tokenHash, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        RideId = rideId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now + ValidFor,
    };

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
