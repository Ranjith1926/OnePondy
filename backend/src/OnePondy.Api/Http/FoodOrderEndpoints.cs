using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.Food;
using OnePondy.Application.Identity;
using OnePondy.Application.Payments;
using OnePondy.Domain.Food;

namespace OnePondy.Api.Http;

public sealed record OrderLineBody(Guid MenuItemId, int Quantity);

public sealed record QuoteBody(Guid BusinessId, string? Area, IReadOnlyList<OrderLineBody>? Lines);

public sealed record PlaceOrderBody(
    string? IdempotencyKey,
    Guid BusinessId,
    string? Area,
    string? Address,
    string? Note,
    string? ContactPhone,
    IReadOnlyList<OrderLineBody>? Lines,
    decimal? ExpectedTotal);

public sealed record OrderVersionBody(uint Version);

public sealed record OrderReasonBody(uint Version, string? Reason);

/// <summary>
/// Food orders (spec Phase 8, steps 8.2 and 8.3): a customer prices a cart, places an order, pays once the restaurant accepts, and may
/// cancel while the restaurant has not started cooking; the restaurant works through its inbox. A payment's result never comes from
/// here: the gateway's webhook, or the server asking the gateway itself, confirms it.
/// </summary>
public static class FoodOrderEndpoints
{
    public static IEndpointRouteBuilder MapFoodOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var customer = app.MapGroup("/api/v1/food").RequireAuthorization().WithTags("Food orders");
        customer.MapPost("/quote", Quote);
        customer.MapPost("/orders", Place);
        customer.MapGet("/orders", ListMine);
        customer.MapGet("/orders/{id:guid}", GetMine);
        customer.MapPost("/orders/{id:guid}/cancel", CancelMine);

        // Safe to repeat: asking again returns the same gateway order while it is open.
        customer.MapPost("/orders/{id:guid}/payment", StartPayment);

        // "Is it paid yet?": the server asks the gateway itself, so a slow webhook never leaves a customer waiting.
        customer.MapPost("/orders/{id:guid}/payment/refresh", RefreshPayment);

        var partner = app.MapGroup("/api/v1/partner/businesses/{businessId:guid}/orders").RequireAuthorization().WithTags("Partner orders");
        partner.MapGet("/", ListForBusiness);
        partner.MapGet("/{id:guid}", GetForBusiness);
        partner.MapPost("/{id:guid}/accept", Accept);
        partner.MapPost("/{id:guid}/reject", Reject);
        partner.MapPost("/{id:guid}/preparing", StartPreparing);
        partner.MapPost("/{id:guid}/ready", MarkReady);
        partner.MapPost("/{id:guid}/cancel", CancelForBusiness);
        return app;
    }

    private static IReadOnlyList<OrderLineInput> ToLines(IReadOnlyList<OrderLineBody>? lines) =>
        [.. (lines ?? []).Select(l => new OrderLineInput(l.MenuItemId, l.Quantity))];

    private static async Task<IResult> Quote(QuoteBody body, OrderService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.QuoteAsync(new QuoteInput(body.BusinessId, body.Area, ToLines(body.Lines)), ct), httpContext);

    private static async Task<IResult> Place(PlaceOrderBody body, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.IdempotencyKey))
        {
            return ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["idempotencyKey"] = ["Required."] }),
                httpContext);
        }

        var input = new PlaceOrderInput(
            body.IdempotencyKey, body.BusinessId, body.Area, body.Address, body.Note, body.ContactPhone, ToLines(body.Lines), body.ExpectedTotal);
        var result = await service.PlaceAsync(user.GetUserId(), input, ct);
        return result.IsSuccess
            ? Results.Created($"/api/v1/food/orders/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> ListMine(ClaimsPrincipal user, OrderService service, CancellationToken ct) =>
        Results.Ok(await service.ListMineAsync(user.GetUserId(), ct));

    private static async Task<IResult> GetMine(Guid id, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetMineAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> CancelMine(
        Guid id, OrderReasonBody body, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CancelMineAsync(id, user.GetUserId(), body.Reason, body.Version, ct), httpContext);

    private static async Task<IResult> StartPayment(Guid id, ClaimsPrincipal user, PaymentService payments, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await payments.StartFoodOrderPaymentAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> RefreshPayment(
        Guid id, ClaimsPrincipal user, PaymentService payments, OrderService orders, HttpContext httpContext, CancellationToken ct)
    {
        var error = await payments.CheckFoodOrderNowAsync(id, user.GetUserId(), ct);
        return error is not null
            ? ProblemResults.From(error, httpContext)
            : EndpointResults.Ok(await orders.GetMineAsync(id, user.GetUserId(), ct), httpContext);
    }

    private static async Task<IResult> ListForBusiness(
        Guid businessId, string? status, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct)
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

        return EndpointResults.Ok(await service.ListForBusinessAsync(businessId, user.GetUserId(), parsed, ct), httpContext);
    }

    private static async Task<IResult> GetForBusiness(
        Guid businessId, Guid id, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetForBusinessAsync(businessId, id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Accept(
        Guid businessId, Guid id, OrderVersionBody body, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AcceptAsync(businessId, id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reject(
        Guid businessId, Guid id, OrderReasonBody body, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.RejectAsync(businessId, id, body.Version, user.GetUserId(), body.Reason, ct), httpContext);

    private static async Task<IResult> StartPreparing(
        Guid businessId, Guid id, OrderVersionBody body, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.StartPreparingAsync(businessId, id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> MarkReady(
        Guid businessId, Guid id, OrderVersionBody body, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.MarkReadyAsync(businessId, id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> CancelForBusiness(
        Guid businessId, Guid id, OrderReasonBody body, ClaimsPrincipal user, OrderService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CancelForBusinessAsync(businessId, id, body.Version, user.GetUserId(), body.Reason, ct), httpContext);
}
