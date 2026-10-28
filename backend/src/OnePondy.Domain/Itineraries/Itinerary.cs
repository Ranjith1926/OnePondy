namespace OnePondy.Domain.Itineraries;

/// <summary>A customer's trip plan: a title, optional dates and an ordered list of stops by day
/// (spec §10, Phase 4). AI-assisted itineraries (Phase 5) start as drafts; manual ones are saved
/// immediately, since there is no separate review step for something the user built by hand.</summary>
public sealed class Itinerary
{
    public const int MaxTitleLength = 180;
    public const int MaxStops = 100;

    private readonly List<ItineraryStop> _stops = [];

    private Itinerary()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public ItineraryStatus Status { get; private set; }

    public ItinerarySource Source { get; private set; }

    /// <summary>Set only while the itinerary is shared; null means the link is off.</summary>
    public string? ShareToken { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ItineraryStop> Stops => _stops;

    public static Itinerary Create(
        Guid userId,
        string title,
        DateRange dates,
        ItinerarySource source,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!dates.IsValid)
        {
            throw new ArgumentException("The end date cannot be before the start date.", nameof(dates));
        }

        return new Itinerary
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Title = title.Trim(),
            StartDate = dates.Start,
            EndDate = dates.End,
            // AI-assisted itineraries need the user's explicit save (spec Phase 5); manual ones do not.
            Status = source == ItinerarySource.AiAssisted ? ItineraryStatus.Draft : ItineraryStatus.Saved,
            Source = source,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Rename(string title, DateRange dates, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!dates.IsValid)
        {
            throw new ArgumentException("The end date cannot be before the start date.", nameof(dates));
        }

        Title = title.Trim();
        StartDate = dates.Start;
        EndDate = dates.End;
        UpdatedAt = now;
    }

    /// <summary>Moves a draft (AI-assisted) itinerary to saved; a no-op once already saved.</summary>
    public void Save(DateTimeOffset now)
    {
        if (Status == ItineraryStatus.Draft)
        {
            Status = ItineraryStatus.Saved;
            UpdatedAt = now;
        }
    }

    public void Archive(DateTimeOffset now)
    {
        Status = ItineraryStatus.Archived;
        UpdatedAt = now;
    }

    public void Unarchive(DateTimeOffset now)
    {
        if (Status == ItineraryStatus.Archived)
        {
            Status = ItineraryStatus.Saved;
            UpdatedAt = now;
        }
    }

    public ItineraryStop AddStop(Guid placeId, int dayNumber, string? notes, DateTimeOffset? plannedArrival, DateTimeOffset now)
    {
        if (_stops.Count >= MaxStops)
        {
            throw new InvalidOperationException($"An itinerary can have at most {MaxStops} stops.");
        }

        if (dayNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(dayNumber), "Day number must be 1 or more.");
        }

        var order = _stops.Where(s => s.DayNumber == dayNumber).Select(s => s.StopOrder).DefaultIfEmpty(-1).Max() + 1;
        var stop = ItineraryStop.Create(Id, placeId, dayNumber, order, notes, plannedArrival, now);
        _stops.Add(stop);
        UpdatedAt = now;
        return stop;
    }

    public void RemoveStop(Guid stopId, DateTimeOffset now)
    {
        var stop = _stops.FirstOrDefault(s => s.Id == stopId);
        if (stop is null)
        {
            return;
        }

        _stops.Remove(stop);
        UpdatedAt = now;
    }

    /// <summary>
    /// Applies a full new day/order for some or all stops in one step, so a drag never leaves stops
    /// half-moved: every position is valid together, or nothing changes (spec §10, Phase 4
    /// acceptance: "reordering itinerary stops never fails or loses a stop").
    /// </summary>
    public void Reorder(IReadOnlyCollection<(Guid StopId, int DayNumber, int StopOrder)> positions, DateTimeOffset now)
    {
        if (positions.Count == 0)
        {
            return;
        }

        if (positions.Select(p => p.StopId).Distinct().Count() != positions.Count)
        {
            throw new ArgumentException("Each stop can appear only once.", nameof(positions));
        }

        var byId = _stops.ToDictionary(s => s.Id);
        foreach (var position in positions)
        {
            if (!byId.ContainsKey(position.StopId))
            {
                throw new ArgumentException($"Stop {position.StopId} does not belong to this itinerary.", nameof(positions));
            }

            if (position.DayNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(positions), "Day number must be 1 or more.");
            }
        }

        foreach (var position in positions)
        {
            byId[position.StopId].MoveTo(position.DayNumber, position.StopOrder);
        }

        UpdatedAt = now;
    }

    /// <summary>A new, unguessable token; replaces any previous one so an old link stops working.</summary>
    public string EnableSharing(Func<string> generateToken, DateTimeOffset now)
    {
        ShareToken = generateToken();
        UpdatedAt = now;
        return ShareToken;
    }

    public void DisableSharing(DateTimeOffset now)
    {
        if (ShareToken is not null)
        {
            ShareToken = null;
            UpdatedAt = now;
        }
    }
}

public sealed class ItineraryStop
{
    public const int MaxNotesLength = 1000;

    private ItineraryStop()
    {
    }

    private ItineraryStop(
        Guid itineraryId,
        Guid placeId,
        int dayNumber,
        int stopOrder,
        string? notes,
        DateTimeOffset? plannedArrival,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        ItineraryId = itineraryId;
        PlaceId = placeId;
        DayNumber = dayNumber;
        StopOrder = stopOrder;
        Notes = notes;
        PlannedArrival = plannedArrival;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid ItineraryId { get; private set; }

    public Guid PlaceId { get; private set; }

    public int DayNumber { get; private set; }

    public int StopOrder { get; private set; }

    public DateTimeOffset? PlannedArrival { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static ItineraryStop Create(
        Guid itineraryId,
        Guid placeId,
        int dayNumber,
        int stopOrder,
        string? notes,
        DateTimeOffset? plannedArrival,
        DateTimeOffset now) =>
        new(itineraryId, placeId, dayNumber, stopOrder, notes, plannedArrival, now);

    internal void MoveTo(int dayNumber, int stopOrder)
    {
        DayNumber = dayNumber;
        StopOrder = stopOrder;
    }
}
