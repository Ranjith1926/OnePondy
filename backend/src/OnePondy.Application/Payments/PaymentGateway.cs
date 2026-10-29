using OnePondy.Application.Common;

namespace OnePondy.Application.Payments;

/// <param name="AmountPaise">Whole paise, the gateway's smallest unit. Money elsewhere is a decimal rupee amount.</param>
/// <param name="Receipt">Our own reference, shown on the gateway dashboard.</param>
public sealed record GatewayOrderRequest(long AmountPaise, string Currency, string Receipt, Guid BookingId);

public sealed record GatewayOrder(string OrderId, long AmountPaise, string Currency);

public sealed record GatewayRefundRequest(string PaymentId, long AmountPaise, string Receipt);

public sealed record GatewayRefund(string RefundId, string Status);

/// <summary>A payment the gateway holds against one of our orders. <c>Status</c> is the gateway's own word, such as <c>captured</c>.</summary>
public sealed record GatewayPayment(string PaymentId, long AmountPaise, string Currency, string Status);

/// <summary>One payment as the gateway lists it, for reconciliation. <c>AmountRefundedPaise</c> is what the gateway has refunded so far.</summary>
public sealed record GatewayPaymentRecord(
    string PaymentId,
    string? OrderId,
    long AmountPaise,
    long AmountRefundedPaise,
    string Currency,
    string Status);

/// <param name="Complete">False when the gateway had more payments than were fetched, so a comparison must not treat "not listed" as "missing".</param>
public sealed record GatewayPaymentPage(IReadOnlyList<GatewayPaymentRecord> Payments, bool Complete);

/// <summary>A refund as the gateway reports it: <c>pending</c>, <c>processed</c> or <c>failed</c>.</summary>
public sealed record GatewayRefundDetail(string RefundId, string? PaymentId, long AmountPaise, string Status);

/// <summary>
/// The payment gateway (spec D3: Razorpay). Only the calls Phase 7 makes. Verifying payment results is done
/// from signed webhooks, never from what the app reports. Implementations never log secrets or card data.
/// </summary>
public interface IPaymentGateway
{
    Task<Result<GatewayOrder>> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken);

    Task<Result<GatewayRefund>> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The payments made against an order, straight from the gateway. Used before a hold is expired, so a payment
    /// whose webhook is late or lost is not thrown away. A failure means "could not find out", not "nothing was paid".
    /// </summary>
    Task<Result<IReadOnlyList<GatewayPayment>>> GetOrderPaymentsAsync(string orderId, CancellationToken cancellationToken);

    /// <summary>Every payment the gateway created in a period, for the daily reconciliation. Read-only.</summary>
    Task<Result<GatewayPaymentPage>> ListPaymentsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The gateway's current view of one refund. Read-only.</summary>
    Task<Result<GatewayRefundDetail>> GetRefundAsync(string refundId, CancellationToken cancellationToken);
}

public static class Money
{
    /// <summary>Rupees to whole paise, exactly: the amounts we store never have more than two decimals.</summary>
    public static long ToPaise(decimal rupees) => decimal.ToInt64(Math.Round(rupees * 100m, MidpointRounding.AwayFromZero));
}

public static class PaymentErrors
{
    public static readonly AppError Unavailable = new(
        "payments_unavailable",
        ErrorKind.Unavailable,
        "Online payment is not available right now. Try again later.");

    public static readonly AppError NotDue = new(
        "payment_not_due",
        ErrorKind.Conflict,
        "This booking is not waiting for payment.");

    public static readonly AppError WindowClosed = new(
        "payment_window_closed",
        ErrorKind.Conflict,
        "The time to pay for this booking has run out.");

    public static readonly AppError UnknownOrder = new(
        "payment_unknown_order",
        ErrorKind.NotFound,
        "No payment attempt matches that gateway order.");

    public static readonly AppError AmountMismatch = new(
        "payment_amount_mismatch",
        ErrorKind.Conflict,
        "The amount paid does not match the amount due, so it was not applied. It needs a manual check.");

    public static readonly AppError AlreadyCaptured = new(
        "payment_already_captured",
        ErrorKind.Conflict,
        "A different payment was already recorded for this order. It needs a manual check.");

    public static readonly AppError Busy = new(
        "payment_busy",
        ErrorKind.Conflict,
        "The booking changed at the same moment. Retry the payment result.");

    public static readonly AppError UnknownRefund = new(
        "payment_unknown_refund",
        ErrorKind.NotFound,
        "No refund matches that gateway refund. It may have been made in the gateway dashboard.");
}

/// <summary>Why a webhook was turned away. The gateway retries anything that is not a 2xx, so these are deliberate.</summary>
public static class WebhookErrors
{
    public static readonly AppError NotConfigured = new(
        "webhook_not_configured",
        ErrorKind.Unavailable,
        "Payment webhooks are not set up on this server.");

    public static readonly AppError InvalidSignature = new(
        "webhook_signature_invalid",
        ErrorKind.Validation,
        "The webhook signature is missing or does not match.");

    public static readonly AppError Malformed = new(
        "webhook_malformed",
        ErrorKind.Validation,
        "The webhook could not be read.");
}
