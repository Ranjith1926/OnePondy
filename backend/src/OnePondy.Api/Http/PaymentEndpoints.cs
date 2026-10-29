using System.Security.Claims;
using OnePondy.Application.Payments;

namespace OnePondy.Api.Http;

/// <summary>Paying for an accepted booking (spec Phase 7). The result of a payment is never taken from here: see the webhook.</summary>
public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        // Safe to repeat: asking again returns the same gateway order while it is open.
        app.MapPost("/api/v1/bookings/{id:guid}/payment", StartPayment)
            .RequireAuthorization()
            .WithTags("Payments");

        return app;
    }

    private static async Task<IResult> StartPayment(
        Guid id,
        ClaimsPrincipal user,
        PaymentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        EndpointResults.Ok(await service.StartPaymentAsync(id, user.GetUserId(), cancellationToken), httpContext);
}
