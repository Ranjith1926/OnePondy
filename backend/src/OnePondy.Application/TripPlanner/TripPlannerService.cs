using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Itineraries;
using OnePondy.Application.Places;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Itineraries;
using OnePondy.Domain.Places;
using OnePondy.Domain.TripPlanner;

namespace OnePondy.Application.TripPlanner;

/// <summary>
/// The AI trip planner workflow (spec §7): restricts candidates to published, verified places,
/// enforces the module flag kill switch plus per-user and daily cost caps, validates the provider's
/// result against the candidate set, and saves what survives as an AI-assisted draft itinerary that
/// the user must still review and save (spec Phase 5 acceptance: never booked or saved automatically).
/// </summary>
public sealed class TripPlannerService(
    IOnePondyDbContext db,
    PlaceCatalogService catalog,
    ItineraryService itineraries,
    IItineraryPlanner planner,
    IOptions<TripPlannerOptions> options,
    TimeProvider time,
    ILogger<TripPlannerService> logger)
{
    private readonly TripPlannerOptions _options = options.Value;

    public async Task<Result<ItineraryDetail>> PlanAsync(
        Guid userId,
        TripPlanRequest request,
        string? language,
        CancellationToken cancellationToken)
    {
        if (request.EndDate < request.StartDate)
        {
            return TripPlannerErrors.InvalidDates;
        }

        var dayCount = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        if (dayCount > _options.MaxDays)
        {
            return TripPlannerErrors.TripTooLong(_options.MaxDays);
        }

        var moduleStatus = await db.ModuleFlags
            .AsNoTracking()
            .Where(f => f.Key == "tripPlanner")
            .Select(f => (ModuleStatus?)f.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (moduleStatus is not ModuleStatus.Available)
        {
            return TripPlannerErrors.PlannerDisabled;
        }

        var now = time.GetUtcNow();
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var todayLogs = await db.TripPlannerRequestLogs
            .Where(l => l.CreatedAt >= dayStart)
            .Select(l => new { l.UserId, l.EstimatedCostUsd })
            .ToListAsync(cancellationToken);

        var spentToday = todayLogs.Sum(l => l.EstimatedCostUsd);
        if (spentToday + _options.EstimatedCostPerRequestUsd > _options.DailyCostCapUsd)
        {
            logger.LogWarning(
                "Trip planner daily cost cap reached ({SpentToday} of {DailyCap})",
                spentToday,
                _options.DailyCostCapUsd);
            return TripPlannerErrors.DailyCapReached;
        }

        if (todayLogs.Count(l => l.UserId == userId) >= _options.DailyRequestLimitPerUser)
        {
            return TripPlannerErrors.RateLimited(dayStart.AddDays(1) - now);
        }

        var near = request.Latitude is { } lat && request.Longitude is { } lng && new GeoPoint(lat, lng).IsValid
            ? new GeoPoint(lat, lng)
            : null;
        var candidates = await catalog.GetCandidatesAsync(request.Interests, near, _options.MaxCandidates, language, cancellationToken);
        if (candidates.Count == 0)
        {
            await LogAsync(userId, now, dayCount, request.Interests, 0, 0, 0m, TripPlannerOutcome.NoCandidates, cancellationToken);
            return TripPlannerErrors.NoCandidates;
        }

        var plannerCandidates = candidates
            .Select(c => new PlannerCandidate(c.Id, c.Name, c.CategoryCode, c.Latitude, c.Longitude))
            .ToList();
        var suggested = await planner.SuggestAsync(request, plannerCandidates, cancellationToken);
        if (!suggested.IsSuccess)
        {
            await LogAsync(userId, now, dayCount, request.Interests, candidates.Count, 0, 0m, TripPlannerOutcome.ProviderUnavailable, cancellationToken);
            return suggested.Error;
        }

        // Anything outside the candidate set, or outside the trip's own days, is discarded rather than
        // trusted: the provider's response is never the source of truth for what places exist (spec §7).
        var candidateIds = plannerCandidates.Select(c => c.PlaceId).ToHashSet();
        var stops = suggested.Value
            .Where(s => candidateIds.Contains(s.PlaceId) && s.DayNumber is >= 1 && s.DayNumber <= dayCount)
            .GroupBy(s => s.PlaceId)
            .Select(g => g.First())
            .OrderBy(s => s.DayNumber)
            .ThenBy(s => s.StopOrder)
            .Take(Itinerary.MaxStops)
            .ToList();

        await LogAsync(
            userId,
            now,
            dayCount,
            request.Interests,
            candidates.Count,
            stops.Count,
            _options.EstimatedCostPerRequestUsd,
            stops.Count > 0 ? TripPlannerOutcome.Suggested : TripPlannerOutcome.NoCandidates,
            cancellationToken);

        if (stops.Count == 0)
        {
            return TripPlannerErrors.NoCandidates;
        }

        var title = $"{dayCount}-day Puducherry trip";
        return await itineraries.CreateAiDraftAsync(
            userId,
            title,
            new DateRange(request.StartDate, request.EndDate),
            [.. stops.Select(s => (s.PlaceId, s.DayNumber))],
            language,
            cancellationToken);
    }

    private async Task LogAsync(
        Guid userId,
        DateTimeOffset now,
        int dayCount,
        IReadOnlyList<string> interests,
        int candidateCount,
        int suggestedCount,
        decimal estimatedCostUsd,
        TripPlannerOutcome outcome,
        CancellationToken cancellationToken)
    {
        db.TripPlannerRequestLogs.Add(new TripPlannerRequestLog(
            userId, now, dayCount, interests, candidateCount, suggestedCount, estimatedCostUsd, outcome));
        await db.SaveChangesAsync(cancellationToken);
    }
}
