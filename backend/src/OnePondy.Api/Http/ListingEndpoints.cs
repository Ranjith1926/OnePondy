using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.Listings;
using OnePondy.Domain.Listings;

namespace OnePondy.Api.Http;

public sealed record ListingApplicationBody(
    string? ListingType,
    string? Title,
    string? Description,
    bool IsBookable,
    decimal? BasePrice,
    int? FreeCancellationHours,
    int? RefundPercentage,
    string? CancellationNotes);

public sealed record UpdateListingBody(uint Version, ListingApplicationBody Listing);

public sealed record ListingVersionBody(uint Version);

public sealed record SendBackListingBody(uint Version, string? Reason);

public sealed record CreateSlotBody(DateTimeOffset StartAt, DateTimeOffset EndAt, int Capacity);

public sealed record UpdateSlotBody(uint Version, int Capacity);

/// <summary>What a partner's business offers, and when it is available (spec §10, Phase 6).</summary>
public static class ListingEndpoints
{
    public static IEndpointRouteBuilder MapListingEndpoints(this IEndpointRouteBuilder app)
    {
        var partner = app.MapGroup("/api/v1/partner/businesses/{businessId:guid}/listings").RequireAuthorization().WithTags("Partner listings");
        partner.MapGet("/", ListMine);
        partner.MapPost("/", Create);
        partner.MapGet("/{id:guid}", GetMine);
        partner.MapPut("/{id:guid}", Update);
        partner.MapPost("/{id:guid}/submit", Submit);
        partner.MapGet("/{id:guid}/slots", ListSlots);
        partner.MapPost("/{id:guid}/slots", CreateSlot);
        partner.MapPut("/{id:guid}/slots/{slotId:guid}", UpdateSlot);
        partner.MapDelete("/{id:guid}/slots/{slotId:guid}", DeleteSlot);

        var admin = app.MapGroup("/api/v1/admin/listings").RequireAuthorization(StaffPolicies.Administration).WithTags("Console listings");
        admin.MapGet("/", AdminList);
        admin.MapGet("/{id:guid}", AdminGet);
        admin.MapPost("/{id:guid}/publish", Publish);
        admin.MapPost("/{id:guid}/send-back", SendBack);
        admin.MapPost("/{id:guid}/suspend", Suspend);
        admin.MapPost("/{id:guid}/reinstate", Reinstate);

        var pub = app.MapGroup("/api/v1/listings").WithTags("Public listings");
        pub.MapGet("/", ListPublic);
        pub.MapGet("/{id:guid}", GetPublic);
        pub.MapGet("/{id:guid}/availability", ListPublicSlots);

        return app;
    }

    private static async Task<IResult> ListMine(Guid businessId, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ListMineAsync(businessId, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> GetMine(Guid businessId, Guid id, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetMineAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Create(
        Guid businessId,
        ListingApplicationBody body,
        ClaimsPrincipal user,
        ListingService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var input = ParseInput(body, httpContext, out var error);
        if (error is not null)
        {
            return error;
        }

        var result = await service.CreateAsync(businessId, user.GetUserId(), input, cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/partner/businesses/{businessId}/listings/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> Update(
        Guid businessId,
        Guid id,
        UpdateListingBody body,
        ClaimsPrincipal user,
        ListingService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var input = ParseInput(body.Listing, httpContext, out var error);
        if (error is not null)
        {
            return error;
        }

        return EndpointResults.Ok(await service.UpdateAsync(id, body.Version, user.GetUserId(), input, ct), httpContext);
    }

    private static async Task<IResult> Submit(Guid businessId, Guid id, ListingVersionBody body, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.SubmitAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> ListSlots(Guid businessId, Guid id, DateTimeOffset? from, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ListSlotsAsync(id, user.GetUserId(), from, ct), httpContext);

    private static async Task<IResult> CreateSlot(Guid businessId, Guid id, CreateSlotBody body, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CreateSlotAsync(id, user.GetUserId(), body.StartAt, body.EndAt, body.Capacity, ct), httpContext);

    private static async Task<IResult> UpdateSlot(
        Guid businessId,
        Guid id,
        Guid slotId,
        UpdateSlotBody body,
        ClaimsPrincipal user,
        ListingService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.UpdateSlotCapacityAsync(id, slotId, body.Version, user.GetUserId(), body.Capacity, ct), httpContext);

    private static async Task<IResult> DeleteSlot(Guid businessId, Guid id, Guid slotId, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.NoContent(await service.DeleteSlotAsync(id, slotId, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> AdminList(string? status, Guid? businessId, int? page, int? pageSize, ListingService service, HttpContext httpContext, CancellationToken ct)
    {
        ListingStatus? parsedStatus = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!EndpointResults.TryParse<ListingStatus>(status, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] }),
                    httpContext);
            }

            parsedStatus = value;
        }

        return Results.Ok(await service.AdminListAsync(new ListingListQuery(businessId, parsedStatus, page, pageSize), ct));
    }

    private static async Task<IResult> AdminGet(Guid id, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AdminGetAsync(id, ct), httpContext);

    private static async Task<IResult> Publish(Guid id, ListingVersionBody body, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.PublishAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> SendBack(Guid id, SendBackListingBody body, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.SendBackAsync(id, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Suspend(Guid id, SendBackListingBody body, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.SuspendAsync(id, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reinstate(Guid id, ListingVersionBody body, ClaimsPrincipal user, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ReinstateAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> ListPublic(string? type, int? page, int? pageSize, ListingService service, HttpContext httpContext, CancellationToken ct)
    {
        ListingType? parsedType = null;
        if (!string.IsNullOrEmpty(type))
        {
            if (!EndpointResults.TryParse<ListingType>(type, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["type"] = ["Unknown listing type."] }),
                    httpContext);
            }

            parsedType = value;
        }

        return EndpointResults.Ok(await service.ListPublicAsync(parsedType, page, pageSize, ct), httpContext);
    }

    private static async Task<IResult> GetPublic(Guid id, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetPublicAsync(id, ct), httpContext);

    private static async Task<IResult> ListPublicSlots(Guid id, DateTimeOffset? from, ListingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ListPublicSlotsAsync(id, from, ct), httpContext);

    private static ListingApplicationInput ParseInput(ListingApplicationBody body, HttpContext httpContext, out IResult? error)
    {
        if (!EndpointResults.TryParse<ListingType>(body.ListingType, out var type))
        {
            error = ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["listingType"] = ["Choose a listing type."] }),
                httpContext);
            return null!;
        }

        error = null;
        return new ListingApplicationInput(
            type,
            body.Title,
            body.Description,
            body.IsBookable,
            body.BasePrice,
            body.FreeCancellationHours,
            body.RefundPercentage,
            body.CancellationNotes);
    }
}
