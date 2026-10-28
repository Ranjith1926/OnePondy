using OnePondy.Application.Common;
using OnePondy.Application.TripPlanner;

namespace OnePondy.Infrastructure.TripPlanner;

/// <summary>Used until an AI provider is configured (spec D5). Reports itself unavailable rather than pretending to plan a trip.</summary>
public sealed class UnavailableItineraryPlanner : IItineraryPlanner
{
    public Task<Result<IReadOnlyList<PlannerSuggestion>>> SuggestAsync(
        TripPlanRequest request,
        IReadOnlyList<PlannerCandidate> candidates,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<PlannerSuggestion>>.Failure(TripPlannerErrors.ProviderUnavailable));
}
