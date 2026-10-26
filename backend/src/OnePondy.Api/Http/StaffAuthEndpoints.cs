using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.Staff;

namespace OnePondy.Api.Http;

public sealed record StaffCodeRequestBody(string? Email);

/// <summary>Web console sign-in. Refresh and sign-out use the shared /api/v1/auth endpoints.</summary>
public static class StaffAuthEndpoints
{
    public static IEndpointRouteBuilder MapStaffAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/admin/auth").RequireRateLimiting(RateLimitingSetup.AuthPolicy).WithTags("Console sign-in");
        auth.MapPost("/otp/request", RequestCode);
        auth.MapPost("/otp/verify", VerifyCode);

        app.MapGet("/api/v1/admin/me", GetMe).RequireAuthorization(StaffPolicies.ContentRead).WithTags("Console sign-in");
        return app;
    }

    private static async Task<IResult> RequestCode(
        StaffCodeRequestBody body,
        StaffSignInService signIn,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await signIn.RequestCodeAsync(body.Email, cancellationToken);
        return result.IsSuccess
            ? Results.Accepted(value: OtpRequestedResponse.From(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> VerifyCode(
        VerifyCodeBody body,
        StaffSignInService signIn,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await signIn.VerifyCodeAsync(body.ChallengeId, body.Code, body.DeviceName, cancellationToken);
        return result.IsSuccess
            ? Results.Ok(SessionResponse.From(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> GetMe(
        ClaimsPrincipal user,
        IStaffDirectory staff,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var member = await staff.FindAsync(user.GetUserId(), cancellationToken);
        return member is null ? ProblemResults.From(StaffErrors.NotFound, httpContext) : Results.Ok(member);
    }
}

internal static class EndpointResults
{
    public static IResult Ok<T>(Result<T> result, HttpContext httpContext) =>
        result.IsSuccess ? Results.Ok(result.Value) : ProblemResults.From(result.Error, httpContext);

    public static IResult NoContent(AppError? error, HttpContext httpContext) =>
        error is null ? Results.NoContent() : ProblemResults.From(error, httpContext);

    /// <summary>Parses an enum from a query or form value written in camelCase, such as "comingSoon".</summary>
    public static bool TryParse<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _);
}
