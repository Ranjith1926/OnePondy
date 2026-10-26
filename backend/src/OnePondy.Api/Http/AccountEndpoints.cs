using System.Security.Claims;
using OnePondy.Application.Identity;

namespace OnePondy.Api.Http;

/// <summary>Account deletion in the app and through the public web page (spec §7).</summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/v1/account").RequireRateLimiting(RateLimitingSetup.AuthPolicy).WithTags("Account");

        account.MapPost("/deletion/code", RequestDeletionCode).RequireAuthorization();
        account.MapPost("/deletion/confirm", ConfirmDeletion).RequireAuthorization();

        // Public: used by /account/delete for people who no longer have the app (Google Play requirement).
        account.MapPost("/deletion-requests", RequestPublicDeletionCode);
        account.MapPost("/deletion-requests/confirm", ConfirmPublicDeletion);

        return app;
    }

    private static async Task<IResult> RequestDeletionCode(
        ClaimsPrincipal user,
        AccountService accounts,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await accounts.RequestDeletionCodeAsync(user.GetUserId(), cancellationToken);
        return result.IsSuccess
            ? Results.Accepted(value: OtpRequestedResponse.From(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> ConfirmDeletion(
        ConfirmCodeBody body,
        ClaimsPrincipal user,
        AccountService accounts,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var error = await accounts.DeleteAccountAsync(user.GetUserId(), body.ChallengeId, body.Code, cancellationToken);
        return error is null ? Results.NoContent() : ProblemResults.From(error, httpContext);
    }

    private static async Task<IResult> RequestPublicDeletionCode(
        OtpRequestBody body,
        AccountService accounts,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await accounts.RequestDeletionCodeForContactAsync(body.Channel, body.Destination, cancellationToken);
        return result.IsSuccess
            ? Results.Accepted(value: OtpRequestedResponse.From(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> ConfirmPublicDeletion(
        ConfirmCodeBody body,
        AccountService accounts,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var error = await accounts.ConfirmDeletionForContactAsync(body.ChallengeId, body.Code, cancellationToken);

        // The same response whether or not an account existed, so the page cannot be used to probe numbers.
        return error is null
            ? Results.Ok(new DeletionRequestStatus("completed"))
            : ProblemResults.From(error, httpContext);
    }
}
