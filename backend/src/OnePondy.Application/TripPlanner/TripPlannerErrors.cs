using OnePondy.Application.Common;

namespace OnePondy.Application.TripPlanner;

public static class TripPlannerErrors
{
    public static readonly AppError PlannerDisabled = new(
        "trip_planner_disabled",
        ErrorKind.Unavailable,
        "The trip planner is not available right now. Build your trip manually instead.");

    public static readonly AppError ProviderUnavailable = new(
        "trip_planner_provider_unavailable",
        ErrorKind.Unavailable,
        "The trip planner could not reach its provider. Build your trip manually instead.");

    public static readonly AppError DailyCapReached = new(
        "trip_planner_daily_cap_reached",
        ErrorKind.Unavailable,
        "The trip planner has reached today's limit. Build your trip manually instead.");

    public static readonly AppError NoCandidates = new(
        "trip_planner_no_candidates",
        ErrorKind.Validation,
        "No verified places match these dates and interests yet. Try different interests, or build your trip manually.");

    public static AppError RateLimited(TimeSpan retryAfter) => new(
        "trip_planner_rate_limited",
        ErrorKind.TooManyRequests,
        "You've asked for several trip plans today. Try again tomorrow, or build your trip manually.",
        RetryAfter: retryAfter);

    public static readonly AppError InvalidDates = AppError.Validation(
        new Dictionary<string, string[]> { ["endDate"] = ["End date must be on or after the start date."] });

    public static AppError TripTooLong(int maxDays) => AppError.Validation(
        new Dictionary<string, string[]> { ["endDate"] = [$"Plan at most {maxDays} days at a time."] });
}
