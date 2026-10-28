using OnePondy.Application.Places;
using OnePondy.Domain.Itineraries;

namespace OnePondy.Application.Itineraries;

public sealed record SavedPlaceView(PublicPlaceSummary Place, DateTimeOffset SavedAt);

public sealed record ItinerarySummary(
    Guid Id,
    string Title,
    DateOnly? StartDate,
    DateOnly? EndDate,
    ItineraryStatus Status,
    ItinerarySource Source,
    bool IsShared,
    int StopCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ItineraryStopView(
    Guid Id,
    int DayNumber,
    int StopOrder,
    string? Notes,
    DateTimeOffset? PlannedArrival,
    PublicPlaceSummary Place);

/// <param name="ShareToken">Null while sharing is off.</param>
public sealed record ItineraryDetail(
    Guid Id,
    string Title,
    DateOnly? StartDate,
    DateOnly? EndDate,
    ItineraryStatus Status,
    ItinerarySource Source,
    string? ShareToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ItineraryStopView> Stops);

/// <summary>What a shared link shows: the plan itself, never who made it (spec §10, Phase 4).</summary>
public sealed record SharedItineraryView(
    string Title,
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<ItineraryStopView> Stops);

public sealed record ItineraryInput(string? Title, DateOnly? StartDate, DateOnly? EndDate);

public sealed record AddStopInput(Guid PlaceId, int? DayNumber, string? Notes, DateTimeOffset? PlannedArrival);

public sealed record StopPositionInput(Guid StopId, int DayNumber, int StopOrder);
