using System.Security.Claims;
using OnePondy.Application.Itineraries;

namespace OnePondy.Api.Http;

public sealed record ItineraryBody(string? Title, DateOnly? StartDate, DateOnly? EndDate)
{
    public ItineraryInput ToInput() => new(Title, StartDate, EndDate);
}

public sealed record AddStopBody(Guid PlaceId, int? DayNumber, string? Notes, DateTimeOffset? PlannedArrival)
{
    public AddStopInput ToInput() => new(PlaceId, DayNumber, Notes, PlannedArrival);
}

public sealed record ReorderStopsBody(IReadOnlyList<StopPositionInput>? Positions);

public sealed record ShareResponse(string ShareToken);

/// <summary>The manual itinerary builder: create, edit, reorder and share a trip plan (spec §10, Phase 4).</summary>
public static class ItineraryEndpoints
{
    public static IEndpointRouteBuilder MapItineraryEndpoints(this IEndpointRouteBuilder app)
    {
        // Public: anyone with the link sees the plan, never who made it.
        app.MapGet("/api/v1/itineraries/shared/{token}", GetShared).WithTags("Itineraries");

        var itineraries = app.MapGroup("/api/v1/itineraries").RequireAuthorization().WithTags("Itineraries");
        itineraries.MapGet("/", List);
        itineraries.MapPost("/", Create);
        itineraries.MapGet("/{id:guid}", Get);
        itineraries.MapPut("/{id:guid}", Update);
        itineraries.MapDelete("/{id:guid}", Delete);
        itineraries.MapPost("/{id:guid}/save", Save);
        itineraries.MapPost("/{id:guid}/archive", Archive);
        itineraries.MapPost("/{id:guid}/unarchive", Unarchive);
        itineraries.MapPost("/{id:guid}/stops", AddStop);
        itineraries.MapDelete("/{id:guid}/stops/{stopId:guid}", RemoveStop);
        itineraries.MapPut("/{id:guid}/stops/order", ReorderStops);
        itineraries.MapPost("/{id:guid}/share", EnableSharing);
        itineraries.MapDelete("/{id:guid}/share", DisableSharing);

        return app;
    }

    private static async Task<IResult> List(
        ClaimsPrincipal user,
        bool? includeArchived,
        ItineraryService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(user.GetUserId(), includeArchived ?? false, cancellationToken));

    private static async Task<IResult> Create(
        ItineraryBody body,
        ClaimsPrincipal user,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(user.GetUserId(), body.ToInput(), Language(lang, httpContext), cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/itineraries/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> Get(
        Guid id,
        ClaimsPrincipal user,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.GetAsync(id, user.GetUserId(), Language(lang, httpContext), ct), httpContext);

    private static async Task<IResult> Update(
        Guid id,
        ItineraryBody body,
        ClaimsPrincipal user,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(
            await service.UpdateAsync(id, user.GetUserId(), body.ToInput(), Language(lang, httpContext), ct),
            httpContext);

    private static async Task<IResult> Delete(
        Guid id,
        ClaimsPrincipal user,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.NoContent(await service.DeleteAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Save(
        Guid id,
        ClaimsPrincipal user,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.ConfirmAsync(id, user.GetUserId(), Language(lang, httpContext), ct), httpContext);

    private static async Task<IResult> Archive(
        Guid id,
        ClaimsPrincipal user,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(
            await service.SetArchivedAsync(id, user.GetUserId(), archived: true, Language(lang, httpContext), ct),
            httpContext);

    private static async Task<IResult> Unarchive(
        Guid id,
        ClaimsPrincipal user,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(
            await service.SetArchivedAsync(id, user.GetUserId(), archived: false, Language(lang, httpContext), ct),
            httpContext);

    private static async Task<IResult> AddStop(
        Guid id,
        AddStopBody body,
        ClaimsPrincipal user,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(
            await service.AddStopAsync(id, user.GetUserId(), body.ToInput(), Language(lang, httpContext), ct),
            httpContext);

    private static async Task<IResult> RemoveStop(
        Guid id,
        Guid stopId,
        ClaimsPrincipal user,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(
            await service.RemoveStopAsync(id, user.GetUserId(), stopId, Language(lang, httpContext), ct),
            httpContext);

    private static async Task<IResult> ReorderStops(
        Guid id,
        ReorderStopsBody body,
        ClaimsPrincipal user,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(
            await service.ReorderStopsAsync(id, user.GetUserId(), body.Positions ?? [], Language(lang, httpContext), ct),
            httpContext);

    private static async Task<IResult> EnableSharing(
        Guid id,
        ClaimsPrincipal user,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.EnableSharingAsync(id, user.GetUserId(), cancellationToken);
        return result.IsSuccess ? Results.Ok(new ShareResponse(result.Value)) : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> DisableSharing(
        Guid id,
        ClaimsPrincipal user,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.NoContent(await service.DisableSharingAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> GetShared(
        string token,
        string? lang,
        ItineraryService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.GetSharedAsync(token, Language(lang, httpContext), cancellationToken);
        return EndpointResults.Ok(result, httpContext);
    }

    private static string? Language(string? lang, HttpContext httpContext) => lang ?? EndpointResults.PreferredLanguage(httpContext);
}
