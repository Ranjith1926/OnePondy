using Microsoft.AspNetCore.Mvc;
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

        // "Is it paid yet?": the server asks the gateway itself, so a slow webhook never leaves a customer waiting.
        app.MapPost("/api/v1/bookings/{id:guid}/payment/refresh", RefreshPayment)
            .RequireAuthorization()
            .WithTags("Payments");

        // Called by Razorpay, not by a signed-in user, so it has no sign-in: the signature is the authentication.
        app.MapPost("/api/v1/webhooks/payments", ReceiveWebhook)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWebhookBytes))
            .WithTags("Payments");

        return app;
    }

    private const int MaxWebhookBytes = 256 * 1024;

    private static async Task<IResult> RefreshPayment(
        Guid id,
        ClaimsPrincipal user,
        PaymentService payments,
        OnePondy.Application.Bookings.BookingService bookings,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var error = await payments.CheckNowAsync(id, user.GetUserId(), cancellationToken);
        return error is not null
            ? ProblemResults.From(error, httpContext)
            : EndpointResults.Ok(await bookings.GetMineAsync(id, user.GetUserId(), cancellationToken), httpContext);
    }

    private static async Task<IResult> ReceiveWebhook(
        PaymentWebhookService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // The signature is over the exact bytes sent, so the body is read raw and never re-serialised.
        using var buffer = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(buffer, cancellationToken);

        var result = await service.HandleAsync(
            buffer.ToArray(),
            httpContext.Request.Headers["X-Razorpay-Signature"].ToString(),
            httpContext.Request.Headers["X-Razorpay-Event-Id"].ToString(),
            cancellationToken);

        return result.IsSuccess
            ? Results.Ok(new { status = result.Value.Outcome.ToString().ToLowerInvariant() })
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> StartPayment(
        Guid id,
        ClaimsPrincipal user,
        PaymentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        EndpointResults.Ok(await service.StartPaymentAsync(id, user.GetUserId(), cancellationToken), httpContext);
}
