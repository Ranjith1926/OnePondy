using OnePondy.Application.Configuration;
using OnePondy.Application.Places;

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
        int? page,
        int? pageSize,
        PlaceCatalogService catalog,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        Results.Ok(await catalog.ListAsync(category, q, lang ?? PreferredLanguage(httpContext), page, pageSize, cancellationToken));

    private static async Task<IResult> Get(
        string idOrSlug,
        string? lang,
        PlaceCatalogService catalog,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var place = await catalog.GetAsync(idOrSlug, lang ?? PreferredLanguage(httpContext), cancellationToken);
        return place is null ? Results.NotFound() : Results.Ok(place);
    }

    /// <summary>The first language in Accept-Language, e.g. "ta" from "ta-IN,ta;q=0.9".</summary>
    private static string? PreferredLanguage(HttpContext httpContext) =>
        httpContext.Request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(language => language.Quality ?? 1)
            .Select(language => language.Value.Value?.Split('-')[0])
            .FirstOrDefault();
}
