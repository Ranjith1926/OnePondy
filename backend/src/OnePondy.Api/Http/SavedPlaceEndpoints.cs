using System.Security.Claims;
using OnePondy.Application.Places;

namespace OnePondy.Api.Http;

public sealed record SavePlaceBody(Guid PlaceId);

/// <summary>A customer's bookmarked places (spec §10, Phase 4).</summary>
public static class SavedPlaceEndpoints
{
    public static IEndpointRouteBuilder MapSavedPlaceEndpoints(this IEndpointRouteBuilder app)
    {
        var saved = app.MapGroup("/api/v1/saved-places").RequireAuthorization().WithTags("Saved places");

        saved.MapGet("/", List);
        saved.MapPost("/", Save);
        saved.MapDelete("/{placeId:guid}", Remove);

        return app;
    }

    private static async Task<IResult> List(
        ClaimsPrincipal user,
        string? lang,
        SavedPlacesService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        Results.Ok(
            await service.ListAsync(user.GetUserId(), lang ?? EndpointResults.PreferredLanguage(httpContext), cancellationToken));

    private static async Task<IResult> Save(
        SavePlaceBody body,
        ClaimsPrincipal user,
        SavedPlacesService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var error = await service.SaveAsync(user.GetUserId(), body.PlaceId, cancellationToken);
        return error is null ? Results.NoContent() : ProblemResults.From(error, httpContext);
    }

    private static async Task<IResult> Remove(
        Guid placeId,
        ClaimsPrincipal user,
        SavedPlacesService service,
        CancellationToken cancellationToken)
    {
        await service.RemoveAsync(user.GetUserId(), placeId, cancellationToken);
        return Results.NoContent();
    }
}
