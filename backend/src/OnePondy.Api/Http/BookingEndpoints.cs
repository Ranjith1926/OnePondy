using System.Security.Claims;
using OnePondy.Application.Bookings;
using OnePondy.Application.Common;
using OnePondy.Domain.Bookings;

namespace OnePondy.Api.Http;

public sealed record CreateBookingBody(Guid ListingId, IReadOnlyList<Guid>? SlotIds, int GuestCount, string? IdempotencyKey);

public sealed record CancelBookingBody(uint Version, string? Reason);

public sealed record RejectBookingBody(uint Version, string? Reason);

public sealed record BookingVersionBody(uint Version);

/// <summary>The booking engine: a customer reserves a slot, a partner accepts or rejects it (spec §7, §10, Phase 6).</summary>
public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var customer = app.MapGroup("/api/v1/bookings").RequireAuthorization().WithTags("Bookings");
        customer.MapPost("/", Create);
        customer.MapGet("/", ListMine);
        customer.MapGet("/{id:guid}", GetMine);
        customer.MapPost("/{id:guid}/cancel", CancelMine);

        var partner = app.MapGroup("/api/v1/partner/businesses/{businessId:guid}/bookings").RequireAuthorization().WithTags("Partner bookings");
        partner.MapGet("/", ListForBusiness);
        partner.MapGet("/{id:guid}", GetForBusiness);
        partner.MapPost("/{id:guid}/accept", Accept);
        partner.MapPost("/{id:guid}/reject", Reject);
        partner.MapPost("/{id:guid}/cancel", CancelForBusiness);
        partner.MapPost("/{id:guid}/complete", Complete);

        return app;
    }

    private static async Task<IResult> Create(
        CreateBookingBody body,
        ClaimsPrincipal user,
        BookingService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.IdempotencyKey))
        {
            return ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["idempotencyKey"] = ["Required."] }),
                httpContext);
        }

        var input = new CreateBookingInput(body.ListingId, body.SlotIds ?? [], body.GuestCount, body.IdempotencyKey);
        var result = await service.CreateAsync(user.GetUserId(), input, cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/bookings/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> ListMine(ClaimsPrincipal user, BookingService service, CancellationToken ct) =>
        Results.Ok(await service.ListMineAsync(user.GetUserId(), ct));

    private static async Task<IResult> GetMine(Guid id, ClaimsPrincipal user, BookingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetMineAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> CancelMine(Guid id, CancelBookingBody body, ClaimsPrincipal user, BookingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CancelMineAsync(id, user.GetUserId(), body.Reason, body.Version, ct), httpContext);

    private static async Task<IResult> ListForBusiness(Guid businessId, string? status, ClaimsPrincipal user, BookingService service, HttpContext httpContext, CancellationToken ct)
    {
        BookingStatus? parsedStatus = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!EndpointResults.TryParse<BookingStatus>(status, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] }),
                    httpContext);
            }

            parsedStatus = value;
        }

        return EndpointResults.Ok(await service.ListForBusinessAsync(businessId, user.GetUserId(), parsedStatus, ct), httpContext);
    }

    private static async Task<IResult> GetForBusiness(Guid businessId, Guid id, ClaimsPrincipal user, BookingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetForBusinessAsync(businessId, id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Accept(Guid businessId, Guid id, BookingVersionBody body, ClaimsPrincipal user, BookingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AcceptAsync(businessId, id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reject(Guid businessId, Guid id, RejectBookingBody body, ClaimsPrincipal user, BookingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.RejectAsync(businessId, id, body.Version, user.GetUserId(), body.Reason, ct), httpContext);

    private static async Task<IResult> CancelForBusiness(Guid businessId, Guid id, CancelBookingBody body, ClaimsPrincipal user, BookingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CancelForBusinessAsync(businessId, id, body.Version, user.GetUserId(), body.Reason, ct), httpContext);

    private static async Task<IResult> Complete(Guid businessId, Guid id, BookingVersionBody body, ClaimsPrincipal user, BookingService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CompleteAsync(businessId, id, body.Version, user.GetUserId(), ct), httpContext);
}
