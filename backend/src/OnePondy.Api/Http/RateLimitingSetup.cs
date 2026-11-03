using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OnePondy.Application.Identity;
using OnePondy.Application.Operations;

namespace OnePondy.Api.Http;

public static class RateLimitingSetup
{
    public const string AuthPolicy = "auth";

    private const string ClassItem = "onepondy.rate_limit_class";

    /// <summary>
    /// Limits how fast anyone can use the API (spec §12 and Phase 10, step 10.4). Sign-in and account endpoints keep their own per-address limit
    /// (the per-number code limits and the daily SMS cap are enforced in the application layer). Every other API request is put in a class
    /// (<see cref="RateLimitClassifier"/>) and counted per signed-in person, or per address for someone who is not signed in, so one person
    /// hammering the API cannot slow it for others and a shared address does not lock out the people signed in behind it. Each limit is a setting.
    /// Health checks, static files and the payment gateway's callbacks are not limited.
    /// </summary>
    public static IServiceCollection AddOnePondyRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>() ?? new RateLimitSettings();
        var authLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 30);
        var authWindow = configuration.GetValue("RateLimiting:Auth:Window", TimeSpan.FromMinutes(1));

        services.AddSingleton<RateLimitStats>();
        services.Replace(ServiceDescriptor.Singleton<IRateLimitStatistics>(sp => sp.GetRequiredService<RateLimitStats>()));

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(AuthPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                Address(httpContext),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authLimit,
                    Window = authWindow,
                    QueueLimit = 0,
                }));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (RateLimitClassifier.IsAuth(httpContext.Request.Path))
                {
                    httpContext.Items[ClassItem] = AuthPolicy;
                    return RateLimitPartition.GetNoLimiter("auth");
                }

                if (Classify(httpContext) is not { } kind)
                {
                    return RateLimitPartition.GetNoLimiter("exempt");
                }

                var name = RateLimitClassifier.Name(kind);
                httpContext.Items[ClassItem] = name;
                var rule = settings.For(kind);
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{name}:{Subject(httpContext)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rule.PermitLimit,
                        Window = rule.Window,
                        QueueLimit = 0,
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                var name = httpContext.Items.TryGetValue(ClassItem, out var item) && item is string known ? known : AuthPolicy;
                httpContext.RequestServices.GetRequiredService<RateLimitStats>().Refused(name);
                httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("OnePondy.RateLimiting")
                    .LogWarning("Rate limit exceeded on {Path} ({Class})", httpContext.Request.Path, name);

                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    : (int)authWindow.TotalSeconds;
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

    /// <summary>Counts each request that belongs to a limited class (and sign-in), once. The limiter itself may look a request over more than once, so it cannot be the one to count.</summary>
    public static IApplicationBuilder UseRateLimitCounting(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var name = RateLimitClassifier.IsAuth(context.Request.Path)
                ? AuthPolicy
                : Classify(context) is { } kind ? RateLimitClassifier.Name(kind) : null;
            if (name is not null)
            {
                context.RequestServices.GetRequiredService<RateLimitStats>().Arrived(name);
            }

            await next();
        });

    private static RateLimitClass? Classify(HttpContext httpContext) =>
        RateLimitClassifier.Classify(
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.User.Identity?.IsAuthenticated == true);

    private static string Address(HttpContext httpContext) => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>The signed-in person, or the address for someone who is not.</summary>
    private static string Subject(HttpContext httpContext) =>
        httpContext.User.FindFirstValue(AuthClaimTypes.Subject) is { Length: > 0 } user ? $"u:{user}" : $"ip:{Address(httpContext)}";
}
