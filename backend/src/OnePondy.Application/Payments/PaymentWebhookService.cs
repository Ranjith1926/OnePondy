using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Payments;

public enum WebhookOutcome
{
    /// <summary>The event was applied.</summary>
    Processed,

    /// <summary>This event id was handled before; nothing changed.</summary>
    Duplicate,

    /// <summary>An event type we do not act on. Recorded and acknowledged.</summary>
    Ignored,

    /// <summary>Valid, but it could not be applied (unknown order, wrong amount...). Recorded for a person to look at.</summary>
    NeedsAttention,
}

public sealed record WebhookResult(WebhookOutcome Outcome, string EventType);

/// <summary>
/// Receives Razorpay's signed webhooks (spec Phase 7, route <c>/api/v1/webhooks/payments</c>). This is the only way a
/// payment result reaches a booking: the app's own report of a payment is never trusted. Every accepted event is
/// stored once by its provider id, so a redelivery changes nothing.
/// </summary>
public sealed class PaymentWebhookService(
    IOnePondyDbContext db,
    PaymentService payments,
    TimeProvider time,
    IOptions<PaymentsOptions> options,
    ILogger<PaymentWebhookService> logger)
{
    private const int MaxEventIdLength = 100;
    private const string Captured = "captured";

    private readonly PaymentsOptions _options = options.Value;

    public async Task<Result<WebhookResult>> HandleAsync(
        ReadOnlyMemory<byte> body,
        string? signature,
        string? providerEventId,
        CancellationToken cancellationToken)
    {
        var secret = _options.Razorpay.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogError("A payment webhook arrived but Payments:Razorpay:WebhookSecret is not set");
            return WebhookErrors.NotConfigured;
        }

        if (!RazorpaySignature.IsValid(secret, body.Span, signature))
        {
            // Not stored: an unauthenticated caller must not be able to fill the table.
            logger.LogWarning("A payment webhook with a missing or wrong signature was refused");
            return WebhookErrors.InvalidSignature;
        }

        if (string.IsNullOrWhiteSpace(providerEventId) || providerEventId.Length > MaxEventIdLength)
        {
            return WebhookErrors.Malformed;
        }

        WebhookPayload payload;
        try
        {
            payload = WebhookPayload.Parse(body.Span);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return WebhookErrors.Malformed;
        }

        var gatewayEvent = await FindAsync(providerEventId, cancellationToken);
        if (gatewayEvent?.ProcessedAt is not null)
        {
            return new WebhookResult(WebhookOutcome.Duplicate, payload.EventType);
        }

        if (gatewayEvent is null)
        {
            gatewayEvent = GatewayEvent.Receive(providerEventId, payload.EventType, payload.ToStoredJson(), true, time.GetUtcNow());
            db.GatewayEvents.Add(gatewayEvent);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The same event arrived on another request at the same moment and won the insert.
                db.GatewayEvents.Remove(gatewayEvent);
                gatewayEvent = await FindAsync(providerEventId, cancellationToken) ?? throw new InvalidOperationException("The webhook event could not be recorded.");
                if (gatewayEvent.ProcessedAt is not null)
                {
                    return new WebhookResult(WebhookOutcome.Duplicate, payload.EventType);
                }
            }
        }

        // An event that was recorded but never finished (the server stopped part way) is applied now. Applying is
        // idempotent, so doing it twice, or in parallel, is safe.
        var applied = await ApplyAsync(payload, cancellationToken);
        if (applied.Error is { } error)
        {
            if (error.Code == PaymentErrors.Busy.Code)
            {
                // Left unprocessed on purpose: a non-2xx answer makes the gateway deliver it again.
                return error;
            }

            gatewayEvent.MarkFailed(error.Code, time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Payment webhook {EventType} could not be applied: {Code}", payload.EventType, error.Code);
            return new WebhookResult(WebhookOutcome.NeedsAttention, payload.EventType);
        }

        gatewayEvent.MarkProcessed(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return new WebhookResult(applied.Value, payload.EventType);
    }

    private Task<GatewayEvent?> FindAsync(string providerEventId, CancellationToken cancellationToken) =>
        db.GatewayEvents.FirstOrDefaultAsync(
            e => e.Provider == PaymentProvider.Razorpay && e.ProviderEventId == providerEventId,
            cancellationToken);

    private async Task<Result<WebhookOutcome>> ApplyAsync(WebhookPayload payload, CancellationToken cancellationToken)
    {
        switch (payload.EventType)
        {
            case "payment.captured":
            case "order.paid":
                if (!string.Equals(payload.Status, Captured, StringComparison.OrdinalIgnoreCase))
                {
                    return WebhookOutcome.Ignored;
                }

                if (payload.OrderId is null || payload.PaymentId is null || payload.AmountPaise is null
                    || !string.Equals(payload.Currency, "INR", StringComparison.OrdinalIgnoreCase))
                {
                    return PaymentErrors.AmountMismatch;
                }

                var captured = await payments.RecordCaptureAsync(
                    payload.OrderId,
                    payload.PaymentId,
                    payload.AmountPaise.Value / 100m,
                    cancellationToken);
                return captured.IsSuccess ? WebhookOutcome.Processed : captured.Error;

            case "payment.failed":
                // The customer may try again on the same order until the payment window ends, so a failed try
                // is recorded but does not end the booking: it ends by payment or by expiry.
                logger.LogInformation("A payment attempt failed at the gateway ({ErrorCode})", payload.ErrorCode ?? "unknown");
                return WebhookOutcome.Processed;

            case "refund.processed":
                return await ApplyRefundAsync(payload, payments.RecordRefundProcessedAsync, cancellationToken);

            case "refund.failed":
                return await ApplyRefundAsync(payload, payments.RecordRefundFailedAsync, cancellationToken);

            default:
                return WebhookOutcome.Ignored;
        }
    }

    private static async Task<Result<WebhookOutcome>> ApplyRefundAsync(
        WebhookPayload payload,
        Func<string, CancellationToken, Task<Result<bool>>> apply,
        CancellationToken cancellationToken)
    {
        if (payload.RefundId is null)
        {
            return PaymentErrors.UnknownRefund;
        }

        var result = await apply(payload.RefundId, cancellationToken);
        return result.IsSuccess ? WebhookOutcome.Processed : result.Error;
    }
}

/// <summary>
/// The few fields of a Razorpay event we act on. The full event also carries the payer's email, phone number and
/// card details; none of that is read or stored, only these identifiers, amounts and statuses.
/// </summary>
public sealed record WebhookPayload(
    string EventType,
    string? PaymentId,
    string? OrderId,
    long? AmountPaise,
    string? Currency,
    string? Status,
    string? Method,
    string? ErrorCode,
    string? RefundId)
{
    public static WebhookPayload Parse(ReadOnlySpan<byte> body)
    {
        var reader = new Utf8JsonReader(body);
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var eventType = root.GetProperty("event").GetString();
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > 64)
        {
            throw new InvalidOperationException("The event has no type.");
        }

        var payment = Entity(root, "payment");
        var refund = Entity(root, "refund");
        var order = Entity(root, "order");

        return new WebhookPayload(
            eventType,
            Text(payment, "id"),
            Text(payment, "order_id") ?? Text(order, "id"),
            Number(payment, "amount") ?? Number(order, "amount_paid"),
            Text(payment, "currency") ?? Text(order, "currency"),
            Text(payment, "status"),
            Text(payment, "method"),
            Text(payment, "error_code"),
            Text(refund, "id"));
    }

    /// <summary>The sanitised record kept in <c>gateway_events</c>.</summary>
    public string ToStoredJson() => JsonSerializer.Serialize(new
    {
        @event = EventType,
        paymentId = PaymentId,
        orderId = OrderId,
        refundId = RefundId,
        amountPaise = AmountPaise,
        currency = Currency,
        status = Status,
        method = Method,
        errorCode = ErrorCode,
    });

    private static JsonElement? Entity(JsonElement root, string name) =>
        root.TryGetProperty("payload", out var payload)
        && payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var holder)
        && holder.ValueKind == JsonValueKind.Object
        && holder.TryGetProperty("entity", out var entity)
        && entity.ValueKind == JsonValueKind.Object
            ? entity
            : null;

    private static string? Text(JsonElement? entity, string name) =>
        entity is { } e && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 100 } text
            ? text
            : null;

    private static long? Number(JsonElement? entity, string name) =>
        entity is { } e && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}
