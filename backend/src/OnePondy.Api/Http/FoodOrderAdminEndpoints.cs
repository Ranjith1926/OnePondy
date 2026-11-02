using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.Delivery;
using OnePondy.Application.Food;
using OnePondy.Application.Identity;
using OnePondy.Domain.Food;

namespace OnePondy.Api.Http;

public sealed record AdminCancelBody(uint Version, string? Reason);

public sealed record AdminReleaseBody(uint Version);

public sealed record DepositBody(decimal? Amount, DateTimeOffset? ReceivedAt, string? Reference, string? Note);

/// <summary>
/// Administrators' monitoring of food orders and deliveries (spec Phase 8, step 8.9): all orders, the ones that look stuck, and the
/// two actions available on a stuck one. Administrator only; every action and every look at a driver's position is audited.
/// </summary>
public static class FoodOrderAdminEndpoints
{
    public static IEndpointRouteBuilder MapFoodOrderAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/food-orders").RequireAuthorization(StaffPolicies.Administration).WithTags("Console food orders");
        group.MapGet("/", List);
        group.MapGet("/attention-count", AttentionCount);
        group.MapGet("/{id:guid}", Get);
        group.MapPost("/{id:guid}/cancel", Cancel);
        group.MapPost("/{id:guid}/release-delivery", Release);

        // Cash on delivery's cash (step 8.8): what each driver holds for OnePondy, and recording what they hand in.
        var cash = app.MapGroup("/api/v1/admin/cash/drivers").RequireAuthorization(StaffPolicies.Administration).WithTags("Console driver cash");
        cash.MapGet("/", ListCash);
        cash.MapPost("/{driverUserId:guid}/deposits", RecordDeposit);
        return app;
    }

    private static async Task<IResult> List(
        string? status,
        bool? needsAttention,
        int? page,
        int? pageSize,
        FoodOrderAdminService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        FoodOrderStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!EndpointResults.TryParse<FoodOrderStatus>(status, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] }),
                    httpContext);
            }

            parsed = value;
        }

        return Results.Ok(await service.ListAsync(new AdminOrderListQuery(parsed, needsAttention, page, pageSize), ct));
    }

    private static async Task<IResult> AttentionCount(FoodOrderAdminService service, CancellationToken ct) =>
        Results.Ok(new { count = await service.AttentionCountAsync(ct) });

    private static async Task<IResult> Get(Guid id, ClaimsPrincipal user, FoodOrderAdminService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Cancel(Guid id, AdminCancelBody body, ClaimsPrincipal user, FoodOrderAdminService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CancelAsync(id, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> ListCash(bool? onlyHolding, CashService service, CancellationToken ct) =>
        Results.Ok(await service.AdminListAsync(onlyHolding ?? true, ct));

    private static async Task<IResult> RecordDeposit(
        Guid driverUserId, DepositBody body, ClaimsPrincipal user, CashService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(
            await service.RecordDepositAsync(driverUserId, new DepositInput(body.Amount, body.ReceivedAt, body.Reference, body.Note), user.GetUserId(), ct),
            httpContext);

    private static async Task<IResult> Release(Guid id, AdminReleaseBody body, ClaimsPrincipal user, FoodOrderAdminService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ReleaseDeliveryAsync(id, body.Version, user.GetUserId(), ct), httpContext);
}
