using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.WebUtilities;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;

namespace OnePondy.Api.Http;

public static class ProblemResults
{
    /// <summary>
    /// Problem details with a stable <c>code</c> the app translates, plus <c>Retry-After</c> when
    /// the client should wait.
    /// </summary>
    public static IResult From(AppError error, HttpContext httpContext)
    {
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
            ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.RetryAfter is { } retryAfter)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            extensions["retryAfterSeconds"] = seconds;
        }

        if (error.Reasons is { Count: > 0 } reasons)
        {
            extensions["reasons"] = reasons;
        }

        return Results.Problem(
            statusCode: status,
            title: ReasonPhrases.GetReasonPhrase(status),
            detail: error.Detail,
            extensions: extensions);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(AuthClaimTypes.Subject)
            ?? throw new InvalidOperationException("The access token has no subject."));

    public static Guid GetSessionId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(AuthClaimTypes.SessionId)
            ?? throw new InvalidOperationException("The access token has no session id."));
}
