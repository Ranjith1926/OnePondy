namespace OnePondy.Domain.TripPlanner;

public enum TripPlannerOutcome
{
    Suggested = 1,
    NoCandidates = 2,
    ProviderUnavailable = 3,
}

/// <summary>
/// One AI trip planner call (spec §7), kept only for the per-user and daily rate limits, the daily
/// cost cap, and quality review. Holds counts and the trip's category/pace/budget inputs, never the
/// user's name, contact details or the free-text mobility notes they typed, so review never needs
/// personal data.
/// </summary>
public sealed class TripPlannerRequestLog
{
    private TripPlannerRequestLog()
    {
    }

    public TripPlannerRequestLog(
        Guid userId,
        DateTimeOffset createdAt,
        int dayCount,
        IReadOnlyList<string> interests,
        int candidateCount,
        int suggestedCount,
        decimal estimatedCostUsd,
        TripPlannerOutcome outcome)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        CreatedAt = createdAt;
        DayCount = dayCount;
        Interests = interests.Count == 0 ? string.Empty : string.Join(',', interests);
        CandidateCount = candidateCount;
        SuggestedCount = suggestedCount;
        EstimatedCostUsd = estimatedCostUsd;
        Outcome = outcome;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int DayCount { get; private set; }

    public string Interests { get; private set; } = string.Empty;

    public int CandidateCount { get; private set; }

    public int SuggestedCount { get; private set; }

    public decimal EstimatedCostUsd { get; private set; }

    public TripPlannerOutcome Outcome { get; private set; }
}
