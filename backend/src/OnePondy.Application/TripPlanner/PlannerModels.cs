using OnePondy.Application.Common;

namespace OnePondy.Application.TripPlanner;

public enum TripPace
{
    Relaxed = 1,
    Balanced = 2,
    Packed = 3,
}

public enum TripBudgetBand
{
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <param name="MobilityNeeds">
/// Free text such as "step-free access only"; used to steer the provider's suggestions but never
/// stored (spec §7: prompts are logged without personal data).
/// </param>
public sealed record TripPlanRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<string> Interests,
    TripPace Pace,
    TripBudgetBand BudgetBand,
    string? MobilityNeeds,
    double? Latitude,
    double? Longitude);

/// <summary>A verified place offered to the provider as a candidate stop; never anything beyond what
/// a guest can already see on its public listing.</summary>
public sealed record PlannerCandidate(Guid PlaceId, string Name, string CategoryCode, double? Latitude, double? Longitude);

public sealed record PlannerSuggestion(Guid PlaceId, int DayNumber, int StopOrder);

/// <summary>
/// Orders and groups candidates into a draft itinerary (spec §7). Implementations must reference only
/// place ids from the given candidate set; <see cref="TripPlannerService"/> discards anything else, so
/// a provider that occasionally hallucinates degrades the plan rather than breaking it.
/// </summary>
public interface IItineraryPlanner
{
    /// <summary>
    /// Returns <see cref="TripPlannerErrors.ProviderUnavailable"/> when the provider cannot be reached,
    /// is not configured, or returns something that cannot be parsed. Never throws for an ordinary
    /// provider failure.
    /// </summary>
    Task<Result<IReadOnlyList<PlannerSuggestion>>> SuggestAsync(
        TripPlanRequest request,
        IReadOnlyList<PlannerCandidate> candidates,
        CancellationToken cancellationToken);
}
