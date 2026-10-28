using OnePondy.Application.Common;
using OnePondy.Application.Configuration;
using OnePondy.Application.Places;
using OnePondy.Domain.Places;

namespace OnePondy.Api.Http;

/// <summary>Public, unauthenticated content for the customer app and website.</summary>
public static class PublicContentEndpoints
{
    public static IEndpointRouteBuilder MapPublicContentEndpoints(this IEndpointRouteBuilder app)
    {
        // Module flags and the minimum app version (spec §8). Short caching keeps flag changes quick.
        app.MapGet("/api/v1/config", async (ConfigService config, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "public, max-age=60";
            return Results.Ok(await config.GetAsync(ct));
        }).WithTags("Config");

        var places = app.MapGroup("/api/v1/places").WithTags("Places");
        places.MapGet("/", List);
        places.MapGet("/{idOrSlug}", Get);
        return app;
    }

    private static async Task<IResult> List(
        string? category,
        string? q,
        string? lang,
        double? lat,
        double? lng,
        int? page,
        int? pageSize,
        PlaceCatalogService catalog,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        GeoPoint? near = null;
        if (lat is { } latitude || lng is { } longitude)
        {
            if (lat is not { } la || lng is not { } lo)
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]>
                    {
                        ["lat"] = ["Provide both lat and lng, or neither."],
                    }),
                    httpContext);
            }

            near = new GeoPoint(la, lo);
            if (!near.IsValid)
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["lat"] = ["Enter a valid latitude and longitude."] }),
                    httpContext);
            }
        }

        var result = await catalog.ListAsync(
            category,
            q,
            lang ?? EndpointResults.PreferredLanguage(httpContext),
            near,
            page,
            pageSize,
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> Get(
        string idOrSlug,
        string? lang,
        PlaceCatalogService catalog,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var place = await catalog.GetAsync(idOrSlug, lang ?? EndpointResults.PreferredLanguage(httpContext), cancellationToken);
        return place is null ? Results.NotFound() : Results.Ok(place);
    }
}
