namespace OnePondy.Application.TripPlanner;

/// <summary>Caps and limits for the AI trip planner (spec §7), bound from configuration so they can be
/// tuned without a release.</summary>
public sealed class TripPlannerOptions
{
    public const string SectionName = "TripPlanner";

    /// <summary>Requests one user can make in a rolling day; keeps one user from exhausting the daily cost cap.</summary>
    public int DailyRequestLimitPerUser { get; set; } = 5;

    /// <summary>Total estimated spend allowed across all users per UTC day; the cost kill switch.</summary>
    public decimal DailyCostCapUsd { get; set; } = 5m;

    /// <summary>Flat per-call estimate, since the provider used here does not return token-level pricing.</summary>
    public decimal EstimatedCostPerRequestUsd { get; set; } = 0.02m;

    /// <summary>Candidates sent to the provider; keeps the prompt small and the result quick to validate.</summary>
    public int MaxCandidates { get; set; } = 40;

    /// <summary>Longest trip the planner will draft in one request.</summary>
    public int MaxDays { get; set; } = 14;
}
