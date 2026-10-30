namespace OnePondy.Domain.Delivery;

public enum DeliveryJobStatus
{
    /// <summary>Waiting for a driver. Any approved driver on duty can take it; the first to accept gets it.</summary>
    Open = 1,

    /// <summary>A driver is on the way to the restaurant.</summary>
    Accepted = 2,

    /// <summary>The driver has the food and is on the way to the customer.</summary>
    PickedUp = 3,

    Delivered = 4,

    /// <summary>The order was cancelled before it arrived.</summary>
    Cancelled = 5,
}

/// <summary>
/// The delivery of one food order (spec Phase 8, step 8.7). It opens when the restaurant marks the order ready and ends when the driver
/// hands it over or the order is cancelled:
/// <code>
/// open     → accepted (a driver takes it) · cancelled
/// accepted → picked_up · open (the driver gives it up before collecting) · cancelled
/// picked_up→ delivered · cancelled
/// </code>
/// The database repeats these rules and also refuses to give an accepted job to a second driver, so two drivers accepting at the same
/// moment cannot both get it. The driver's last position is kept on the job only while it is accepted or picked up, and is erased
/// when the job ends (the database refuses to keep it any longer).
/// </summary>
public sealed class DeliveryJob
{
    public const int MaxReasonLength = 500;

    private DeliveryJob()
    {
    }

    public Guid Id { get; private set; }

    public Guid FoodOrderId { get; private set; }

    public DeliveryJobStatus Status { get; private set; }

    /// <summary>The driver who has the job. Empty while open; kept after delivery as the record of who delivered.</summary>
    public Guid? DriverUserId { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? PickedUpAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelReason { get; private set; }

    public double? LastLatitude { get; private set; }

    public double? LastLongitude { get; private set; }

    public DateTimeOffset? LastLocationAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    /// <summary>Whether a driver holds the job right now.</summary>
    public bool IsActive => Status is DeliveryJobStatus.Accepted or DeliveryJobStatus.PickedUp;

    public static DeliveryJob Open(Guid foodOrderId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        FoodOrderId = foodOrderId,
        Status = DeliveryJobStatus.Open,
        OpenedAt = now,
        UpdatedAt = now,
    };

    public void Accept(Guid driverUserId, DateTimeOffset now)
    {
        Require(DeliveryJobStatus.Open);
        Status = DeliveryJobStatus.Accepted;
        DriverUserId = driverUserId;
        AcceptedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The driver gives the job up before collecting the food, and it goes back to the other drivers.</summary>
    public void Release(Guid driverUserId, DateTimeOffset now)
    {
        RequireDriver(driverUserId, DeliveryJobStatus.Accepted);
        Status = DeliveryJobStatus.Open;
        DriverUserId = null;
        AcceptedAt = null;
        ClearLocation();
        UpdatedAt = now;
    }

    /// <summary>An administrator takes an uncollected delivery from its driver and offers it to the others again.</summary>
    public void AdminRelease(DateTimeOffset now)
    {
        Require(DeliveryJobStatus.Accepted);
        Status = DeliveryJobStatus.Open;
        DriverUserId = null;
        AcceptedAt = null;
        ClearLocation();
        UpdatedAt = now;
    }

    public void PickUp(Guid driverUserId, DateTimeOffset now)
    {
        RequireDriver(driverUserId, DeliveryJobStatus.Accepted);
        Status = DeliveryJobStatus.PickedUp;
        PickedUpAt = now;
        UpdatedAt = now;
    }

    public void Deliver(Guid driverUserId, DateTimeOffset now)
    {
        RequireDriver(driverUserId, DeliveryJobStatus.PickedUp);
        Status = DeliveryJobStatus.Delivered;
        DeliveredAt = now;
        ClearLocation();
        UpdatedAt = now;
    }

    /// <summary>The order was cancelled, so the delivery is off. Whoever had it keeps the record, but their position is erased.</summary>
    public void Cancel(string? reason, DateTimeOffset now)
    {
        if (Status is not (DeliveryJobStatus.Open or DeliveryJobStatus.Accepted or DeliveryJobStatus.PickedUp))
        {
            throw new InvalidOperationException($"A delivery that is {Status} cannot be cancelled.");
        }

        Status = DeliveryJobStatus.Cancelled;
        CancelledAt = now;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ClearLocation();
        UpdatedAt = now;
    }

    /// <summary>Keeps only the latest position, and only while the driver holds the job.</summary>
    public void RecordLocation(Guid driverUserId, double latitude, double longitude, DateTimeOffset now)
    {
        if (!IsActive || DriverUserId != driverUserId)
        {
            throw new InvalidOperationException("Location is only shared during your own delivery.");
        }

        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "That is not a position on Earth.");
        }

        LastLatitude = latitude;
        LastLongitude = longitude;
        LastLocationAt = now;
        UpdatedAt = now;
    }

    private void ClearLocation()
    {
        LastLatitude = null;
        LastLongitude = null;
        LastLocationAt = null;
    }

    private void Require(DeliveryJobStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"This delivery is {Status}, not {expected}.");
        }
    }

    private void RequireDriver(Guid driverUserId, DeliveryJobStatus expected)
    {
        if (DriverUserId != driverUserId)
        {
            throw new InvalidOperationException("This delivery is not assigned to you.");
        }

        Require(expected);
    }
}
