using System.Globalization;
using System.Threading.RateLimiting;

namespace OnePondy.Api.Http;

public static class RateLimitingSetup
{
    public const string AuthPolicy = "auth";

    /// <summary>
    /// Per-IP fixed-window limit on authentication and account endpoints (spec §12). Per-destination
    /// code limits and the daily SMS cap are enforced in the application layer. The default leaves room
    /// for many people sharing one carrier IP address.
    /// </summary>
    public static IServiceCollection AddOnePondyRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 30);
        var window = configuration.GetValue("RateLimiting:Auth:Window", TimeSpan.FromMinutes(1));

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(AuthPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0,
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("OnePondy.RateLimiting")
                    .LogWarning("Rate limit exceeded on {Path}", httpContext.Request.Path);

                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    : (int)window.TotalSeconds;
                httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

                await Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too Many Requests",
                    detail: "Too many requests. Wait before trying again.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "rate_limited",
                        ["retryAfterSeconds"] = seconds,
                    }).ExecuteAsync(httpContext);
            };
        });

        return services;
    }
}
