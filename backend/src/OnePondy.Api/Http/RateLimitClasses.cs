using System.Collections.Concurrent;
using OnePondy.Application.Operations;

namespace OnePondy.Api.Http;

/// <summary>
/// The kinds of request that are limited separately (spec Phase 10, step 10.4, decision R12). Reading is cheap, so it gets a generous limit;
/// writing gets a tighter one; creating something a person must then read, moderate or answer (a review, a request for help, a booking,
/// an order) the tightest; uploads, which are heavy, their own.
/// </summary>
public enum RateLimitClass
{
    /// <summary>Public reads: places, listings, restaurants, reviews, configuration.</summary>
    Browse,

    /// <summary>Reads by a signed-in person.</summary>
    Read,

    /// <summary>Changes by a person: saving, cancelling, accepting, updating.</summary>
    Write,

    /// <summary>Things that start something: a review, a support request or message, a booking, an order, a trip plan, a business application.</summary>
    Create,

    /// <summary>Photos and documents.</summary>
    Upload,

    /// <summary>Everything in the console's staff area.</summary>
    Staff,
}

/// <summary>Decides which class a request belongs to, from what is known before the endpoint runs. Null means this limiter does not apply.</summary>
public static class RateLimitClassifier
{
    /// <summary>Sign-in and account endpoints have their own limit (<see cref="RateLimitingSetup.AuthPolicy"/>), counted per address and tighter.</summary>
    private static readonly string[] AuthPrefixes = ["/api/v1/auth", "/api/v1/account", "/api/v1/admin/auth"];

    /// <summary>Requests that start something, matched exactly (a trailing slash is ignored).</summary>
    private static readonly HashSet<string> CreatePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/reviews",
        "/api/v1/support/tickets",
        "/api/v1/bookings",
        "/api/v1/food/orders",
        "/api/v1/rides",
        "/api/v1/rides/quotes",
        "/api/v1/trip-planner/plan",
        "/api/v1/partner/businesses",
        "/api/v1/devices",
    };

    public static RateLimitClass? Classify(string method, PathString path, bool authenticated)
    {
        var value = (path.Value ?? "/").TrimEnd('/');

        // Health checks, static files, the API description: not the API's business traffic. The payment gateway's callbacks are verified by
        // signature and must never be refused for arriving together.
        if (!value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/v1/webhooks", StringComparison.OrdinalIgnoreCase)
            || AuthPrefixes.Any(prefix => IsUnder(value, prefix)))
        {
            return null;
        }

        var reading = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
        if (!reading && IsUpload(method, value))
        {
            return RateLimitClass.Upload;
        }

        if (IsUnder(value, "/api/v1/admin"))
        {
            return RateLimitClass.Staff;
        }

        if (reading)
        {
            return authenticated ? RateLimitClass.Read : RateLimitClass.Browse;
        }

        return IsCreate(method, value) ? RateLimitClass.Create : RateLimitClass.Write;
    }

    /// <summary>Whether the request is to sign-in or account endpoints, which the named <see cref="RateLimitingSetup.AuthPolicy"/> limits.</summary>
    public static bool IsAuth(PathString path) =>
        AuthPrefixes.Any(prefix => IsUnder((path.Value ?? "/").TrimEnd('/'), prefix));

    private static bool IsUnder(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

    private static bool IsUpload(string method, string path) =>
        HttpMethods.IsPost(method)
        && (path.EndsWith("/media", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/documents", StringComparison.OrdinalIgnoreCase));

    private static bool IsCreate(string method, string path) =>
        HttpMethods.IsPost(method)
        && (CreatePaths.Contains(path)
            || (path.StartsWith("/api/v1/support/tickets/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("/messages", StringComparison.OrdinalIgnoreCase)));

    public static string Name(RateLimitClass value) => value.ToString().ToLowerInvariant();
}

/// <summary>One limit: this many requests in this window, per person (or per address, for someone who is not signed in).</summary>
public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>The limits (settings <c>RateLimiting:Browse</c>, <c>Read</c>, <c>Write</c>, <c>Create</c>, <c>Upload</c> and <c>Staff</c>), with defaults that leave room for ordinary use, including many people behind one address.</summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    public RateLimitRule Browse { get; set; } = new() { PermitLimit = 120 };

    public RateLimitRule Read { get; set; } = new() { PermitLimit = 240 };

    public RateLimitRule Write { get; set; } = new() { PermitLimit = 90 };

    public RateLimitRule Create { get; set; } = new() { PermitLimit = 12 };

    public RateLimitRule Upload { get; set; } = new() { PermitLimit = 20 };

    public RateLimitRule Staff { get; set; } = new() { PermitLimit = 600 };

    public RateLimitRule For(RateLimitClass value) => value switch
    {
        RateLimitClass.Browse => Browse,
        RateLimitClass.Read => Read,
        RateLimitClass.Write => Write,
        RateLimitClass.Create => Create,
        RateLimitClass.Upload => Upload,
        _ => Staff,
    };
}

/// <summary>Counts what the limiter does, per class, for the operations page. Thread-safe, memory only, no addresses or accounts.</summary>
public sealed class RateLimitStats(TimeProvider time) : IRateLimitStatistics
{
    private sealed class Counter
    {
        public long Requests;

        public long Rejected;

        public long LastRejectedTicks;
    }

    private readonly ConcurrentDictionary<string, Counter> _classes = new();
    private readonly DateTimeOffset _since = time.GetUtcNow();

    /// <summary>A request of this class arrived (whether or not it is then allowed).</summary>
    public void Arrived(string name) => Interlocked.Increment(ref _classes.GetOrAdd(name, _ => new Counter()).Requests);

    /// <summary>A request of this class was refused for coming too fast.</summary>
    public void Refused(string name)
    {
        var counter = _classes.GetOrAdd(name, _ => new Counter());
        Interlocked.Increment(ref counter.Rejected);
        Interlocked.Exchange(ref counter.LastRejectedTicks, time.GetUtcNow().UtcTicks);
    }

    public RateLimitSnapshot Snapshot() => new(
        _since,
        [
            .. _classes
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new RateLimitClassStatistics(
                    entry.Key,
                    Interlocked.Read(ref entry.Value.Requests),
                    Interlocked.Read(ref entry.Value.Rejected),
                    Interlocked.Read(ref entry.Value.LastRejectedTicks) is var ticks and > 0
                        ? new DateTimeOffset(ticks, TimeSpan.Zero)
                        : null)),
        ]);
}
