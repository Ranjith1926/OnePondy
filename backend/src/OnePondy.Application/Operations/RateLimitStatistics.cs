namespace OnePondy.Application.Operations;

/// <summary>How one kind of request has fared since the API last started: how many arrived and how many were refused for coming too fast.</summary>
public sealed record RateLimitClassStatistics(string Name, long Requests, long Rejected, DateTimeOffset? LastRejectedAt);

public sealed record RateLimitSnapshot(DateTimeOffset Since, IReadOnlyList<RateLimitClassStatistics> Classes);

/// <summary>
/// What the rate limiter reports about itself (spec Phase 10, step 10.4). Counts only: no address and no account is kept, so the numbers
/// say "something is hammering sign-in" without recording who. They live in the API's memory and start again from nothing when it restarts.
/// </summary>
public interface IRateLimitStatistics
{
    RateLimitSnapshot Snapshot();
}

/// <summary>Used where there is no limiter to ask (the Worker): it reports nothing.</summary>
public sealed class NoRateLimitStatistics(TimeProvider time) : IRateLimitStatistics
{
    private readonly DateTimeOffset _since = time.GetUtcNow();

    public RateLimitSnapshot Snapshot() => new(_since, []);
}
