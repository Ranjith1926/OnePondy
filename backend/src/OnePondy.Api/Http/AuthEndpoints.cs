using System.Security.Claims;
using OnePondy.Application.Identity;

namespace OnePondy.Api.Http;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting(RateLimitingSetup.AuthPolicy).WithTags("Auth");
        auth.MapPost("/otp/request", RequestCode);
        auth.MapPost("/otp/verify", VerifyCode);
        auth.MapPost("/refresh", Refresh);
        auth.MapPost("/logout", SignOut).RequireAuthorization();

        var profile = app.MapGroup("/api/v1/profile").RequireAuthorization().WithTags("Profile");
        profile.MapGet("/", GetProfile);
        profile.MapPatch("/", UpdateProfile);

        return app;
    }

    private static async Task<IResult> RequestCode(
        OtpRequestBody body,
        AuthService auth,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await auth.RequestSignInCodeAsync(body.Channel, body.Destination, cancellationToken);
        return result.IsSuccess
            ? Results.Accepted(value: OtpRequestedResponse.From(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> VerifyCode(
        VerifyCodeBody body,
        AuthService auth,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await auth.VerifySignInCodeAsync(body.ChallengeId, body.Code, body.DeviceName, cancellationToken);
        return result.IsSuccess
            ? Results.Ok(SessionResponse.From(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> Refresh(
        RefreshBody body,
        AuthService auth,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await auth.RefreshAsync(body.RefreshToken, cancellationToken);
        return result.IsSuccess
            ? Results.Ok(SessionResponse.From(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> SignOut(ClaimsPrincipal user, AuthService auth, CancellationToken cancellationToken)
    {
        await auth.SignOutAsync(user.GetSessionId(), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetProfile(
        ClaimsPrincipal user,
        AccountService accounts,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await accounts.GetProfileAsync(user.GetUserId(), cancellationToken);
        return result.IsSuccess
            ? Results.Ok(ProfileResponse.From(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> UpdateProfile(
        UpdateProfileBody body,
        ClaimsPrincipal user,
        AccountService accounts,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await accounts.UpdateProfileAsync(
            user.GetUserId(),
            body.DisplayName,
            body.PreferredLanguage,
            cancellationToken);
        return result.IsSuccess
            ? Results.Ok(ProfileResponse.From(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }
}
