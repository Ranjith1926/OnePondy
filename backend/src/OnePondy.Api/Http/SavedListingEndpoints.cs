using System.Security.Claims;
using OnePondy.Application.Listings;

namespace OnePondy.Api.Http;

public sealed record SaveListingBody(Guid ListingId);

/// <summary>A customer's bookmarked listings (spec §10, Phase 6).</summary>
public static class SavedListingEndpoints
{
    public static IEndpointRouteBuilder MapSavedListingEndpoints(this IEndpointRouteBuilder app)
    {
        var saved = app.MapGroup("/api/v1/saved-listings").RequireAuthorization().WithTags("Saved listings");

        saved.MapGet("/", List);
        saved.MapPost("/", Save);
        saved.MapDelete("/{listingId:guid}", Remove);

        return app;
    }

    private static async Task<IResult> List(ClaimsPrincipal user, SavedListingsService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(user.GetUserId(), cancellationToken));

    private static async Task<IResult> Save(
        SaveListingBody body,
        ClaimsPrincipal user,
        SavedListingsService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var error = await service.SaveAsync(user.GetUserId(), body.ListingId, cancellationToken);
        return error is null ? Results.NoContent() : ProblemResults.From(error, httpContext);
    }

    private static async Task<IResult> Remove(
        Guid listingId,
        ClaimsPrincipal user,
        SavedListingsService service,
        CancellationToken cancellationToken)
    {
        await service.RemoveAsync(user.GetUserId(), listingId, cancellationToken);
        return Results.NoContent();
    }
}
