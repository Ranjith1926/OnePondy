namespace OnePondy.Domain.Listings;

/// <summary>
/// A bookable window for a listing (spec §10, Phase 6): one night for a stay, one session for an
/// activity, event or service. Capacity is reserved with optimistic concurrency, so two customers
/// racing for the last place can never both win.
/// </summary>
public sealed class AvailabilitySlot
{
    private AvailabilitySlot()
    {
    }

    public Guid Id { get; private set; }

    public Guid ListingId { get; private set; }

    public DateTimeOffset StartAt { get; private set; }

    public DateTimeOffset EndAt { get; private set; }

    public int Capacity { get; private set; }

    public int ReservedCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public int AvailableCount => Capacity - ReservedCount;

    public static AvailabilitySlot Create(Guid listingId, DateTimeOffset startAt, DateTimeOffset endAt, int capacity, DateTimeOffset now)
    {
        if (endAt <= startAt)
        {
            throw new ArgumentException("The slot must end after it starts.", nameof(endAt));
        }

        if (capacity < 1)
        {
            throw new ArgumentException("Capacity must be at least 1.", nameof(capacity));
        }

        return new AvailabilitySlot
        {
            Id = Guid.CreateVersion7(),
            ListingId = listingId,
            StartAt = startAt,
            EndAt = endAt,
            Capacity = capacity,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdateCapacity(int capacity, DateTimeOffset now)
    {
        if (capacity < ReservedCount)
        {
            throw new InvalidOperationException("Capacity cannot be reduced below what is already reserved.");
        }

        Capacity = capacity;
        UpdatedAt = now;
    }

    /// <summary>Reserves inventory for a new booking; throws if there is not enough room left.</summary>
    public void Reserve(int quantity, DateTimeOffset now)
    {
        if (quantity < 1)
        {
            throw new ArgumentException("Quantity must be at least 1.", nameof(quantity));
        }

        if (ReservedCount + quantity > Capacity)
        {
            throw new InvalidOperationException("Not enough availability left in this slot.");
        }

        ReservedCount += quantity;
        UpdatedAt = now;
    }

    /// <summary>Releases inventory from a booking that was cancelled, rejected or expired.</summary>
    public void Release(int quantity, DateTimeOffset now)
    {
        ReservedCount = Math.Max(0, ReservedCount - quantity);
        UpdatedAt = now;
    }
}
