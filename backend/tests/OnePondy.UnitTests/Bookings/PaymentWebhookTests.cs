using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Bookings;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

public class RazorpaySignatureTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"event":"payment.captured"}""");

    [Fact]
    public void A_signature_computed_with_the_secret_is_valid()
    {
        var signature = RazorpaySignature.Compute("whsec", Body);

        Assert.Matches("^[0-9a-f]{64}$", signature);
        Assert.True(RazorpaySignature.IsValid("whsec", Body, signature));
        Assert.True(RazorpaySignature.IsValid("whsec", Body, signature.ToUpperInvariant()));
    }

    [Fact]
    public void The_signature_is_the_standard_hmac_sha256_of_the_body_in_lower_case_hex()
    {
        // The widely published HMAC-SHA256 test vector, so a change of algorithm or encoding is noticed.
        Assert.Equal(
            "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8",
            RazorpaySignature.Compute("key", Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("deadbeef")]
    public void A_missing_or_wrong_signature_is_invalid(string? signature)
    {
        Assert.False(RazorpaySignature.IsValid("whsec", Body, signature));
    }

    [Fact]
    public void A_signature_made_with_another_secret_or_for_another_body_is_invalid()
    {
        var other = RazorpaySignature.Compute("different", Body);
        var forOtherBody = RazorpaySignature.Compute("whsec", Encoding.UTF8.GetBytes("""{"event":"payment.failed"}"""));

        Assert.False(RazorpaySignature.IsValid("whsec", Body, other));
        Assert.False(RazorpaySignature.IsValid("whsec", Body, forOtherBody));
    }

    [Fact]
    public void Without_a_secret_nothing_is_valid()
    {
        Assert.False(RazorpaySignature.IsValid(null, Body, RazorpaySignature.Compute("whsec", Body)));
        Assert.False(RazorpaySignature.IsValid("", Body, RazorpaySignature.Compute("", Body)));
    }
}

/// <summary>Phase 7 step 7.3: the webhook that turns a gateway result into a booking change.</summary>
public class PaymentWebhookTests
{
    private const string Secret = "unit-webhook-secret";
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();
    private int _eventSeq;

    private PaymentWebhookService Webhooks(string? secret = Secret)
    {
        _h.Bookings.Payments.Razorpay.WebhookSecret = secret;
        return new PaymentWebhookService(
            _h.Bookings.Db,
            _h.Payments,
            _h.Bookings.Time,
            Options.Create(_h.Bookings.Payments),
            NullLogger<PaymentWebhookService>.Instance);
    }

    private static (byte[] Body, string Signature) Sign(string json, string secret = Secret)
    {
        var body = Encoding.UTF8.GetBytes(json);
        return (body, RazorpaySignature.Compute(secret, body));
    }

    private static string Captured(string orderId, string paymentId, long paise, string eventType = "payment.captured", string status = "captured") =>
        Event(eventType, new
        {
            payment = Entity(new
            {
                id = paymentId,
                order_id = orderId,
                amount = paise,
                currency = "INR",
                status,
                method = "upi",
                email = "customer@example.com",
                contact = "+919876543210",
                vpa = "someone@okbank",
                card = new { last4 = "4242", network = "Visa" },
            }),
        });

    private static object Entity(object entity) => new { entity };

    private static string Event(string eventType, object payload) =>
        System.Text.Json.JsonSerializer.Serialize(new { entity = "event", @event = eventType, payload });

    private static string RefundEvent(string eventType, string refundId) =>
        Event(eventType, new { refund = Entity(new { id = refundId, payment_id = "pay_1", amount = 100000, status = "processed" }) });

    private async Task<Application.Common.Result<WebhookResult>> SendAsync(string json, string eventId, string? secret = Secret, string? signatureOverride = null)
    {
        var (body, signature) = Sign(json);
        return await Webhooks(secret).HandleAsync(body, signatureOverride ?? signature, eventId, None);
    }

    private string NextEventId() => $"evt_{++_eventSeq}";

    private async Task<(BookingDetail Booking, string OrderId)> OpenPaymentAsync()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        return (booking, started.OrderId);
    }

    // --- Refusing what cannot be trusted ---

    [Fact]
    public async Task A_webhook_with_a_wrong_signature_changes_nothing_and_is_not_stored()
    {
        var (booking, orderId) = await OpenPaymentAsync();

        var result = await SendAsync(Captured(orderId, "pay_1", 100_000), NextEventId(), signatureOverride: "0000");

        Assert.Equal("webhook_signature_invalid", result.Error!.Code);
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal(0, await _h.Bookings.Db.GatewayEvents.CountAsync());
    }

    [Fact]
    public async Task A_body_changed_after_signing_is_refused()
    {
        var (_, orderId) = await OpenPaymentAsync();
        var (_, signature) = Sign(Captured(orderId, "pay_1", 100_000));
        var tampered = Encoding.UTF8.GetBytes(Captured(orderId, "pay_1", 1));

        var result = await Webhooks().HandleAsync(tampered, signature, NextEventId(), None);

        Assert.Equal("webhook_signature_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Without_a_configured_secret_every_webhook_is_refused()
    {
        var (_, orderId) = await OpenPaymentAsync();

        var result = await SendAsync(Captured(orderId, "pay_1", 100_000), NextEventId(), secret: null);

        Assert.Equal("webhook_not_configured", result.Error!.Code);
        Assert.Equal(0, await _h.Bookings.Db.GatewayEvents.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_signed_webhook_without_an_event_id_is_refused(string? eventId)
    {
        var (body, signature) = Sign("""{"event":"payment.captured"}""");

        var result = await Webhooks().HandleAsync(body, signature, eventId, None);

        Assert.Equal("webhook_malformed", result.Error!.Code);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"payload":{}}""")]
    [InlineData("""{"event":""}""")]
    public async Task A_signed_body_that_is_not_an_event_is_refused(string json)
    {
        var result = await SendAsync(json, NextEventId());

        Assert.Equal("webhook_malformed", result.Error!.Code);
    }

    // --- Applying a payment ---

    [Fact]
    public async Task A_signed_payment_captured_event_confirms_the_booking()
    {
        var (booking, orderId) = await OpenPaymentAsync();

        var result = await SendAsync(Captured(orderId, "pay_1", 100_000), NextEventId());

        Assert.Equal(WebhookOutcome.Processed, result.Value!.Outcome);
        Assert.Equal(BookingStatus.Confirmed, (await _h.BookingAsync(booking.Id)).Status);
        var stored = await _h.Bookings.Db.GatewayEvents.SingleAsync();
        Assert.NotNull(stored.ProcessedAt);
        Assert.Null(stored.Error);
        Assert.True(stored.SignatureValid);
    }

    [Fact]
    public async Task The_same_event_delivered_again_is_acknowledged_without_doing_anything()
    {
        var (booking, orderId) = await OpenPaymentAsync();
        await SendAsync(Captured(orderId, "pay_1", 100_000), "evt_same");
        var history = (await _h.BookingAsync(booking.Id)).History.Count;

        var again = await SendAsync(Captured(orderId, "pay_1", 100_000), "evt_same");

        Assert.Equal(WebhookOutcome.Duplicate, again.Value!.Outcome);
        Assert.Equal(history, (await _h.BookingAsync(booking.Id)).History.Count);
        Assert.Equal(1, await _h.Bookings.Db.GatewayEvents.CountAsync());
        Assert.Empty(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task Razorpay_sending_order_paid_as_well_as_payment_captured_confirms_only_once()
    {
        var (booking, orderId) = await OpenPaymentAsync();
        await SendAsync(Captured(orderId, "pay_1", 100_000), NextEventId());
        var history = (await _h.BookingAsync(booking.Id)).History.Count;

        var second = await SendAsync(Captured(orderId, "pay_1", 100_000, eventType: "order.paid"), NextEventId());

        Assert.Equal(WebhookOutcome.Processed, second.Value!.Outcome);
        Assert.Equal(history, (await _h.BookingAsync(booking.Id)).History.Count);
        Assert.Equal(2, await _h.Bookings.Db.GatewayEvents.CountAsync());
        Assert.Empty(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task A_payment_for_the_wrong_amount_is_recorded_for_a_person_to_look_at_and_not_applied()
    {
        var (booking, orderId) = await OpenPaymentAsync();

        var result = await SendAsync(Captured(orderId, "pay_1", 99_999), NextEventId());

        Assert.Equal(WebhookOutcome.NeedsAttention, result.Value!.Outcome);
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);
        var stored = await _h.Bookings.Db.GatewayEvents.SingleAsync();
        Assert.Equal("payment_amount_mismatch", stored.Error);
        Assert.NotNull(stored.ProcessedAt);
    }

    [Fact]
    public async Task A_payment_for_an_order_we_never_made_is_recorded_and_acknowledged()
    {
        var result = await SendAsync(Captured("order_not_ours", "pay_1", 100_000), NextEventId());

        Assert.Equal(WebhookOutcome.NeedsAttention, result.Value!.Outcome);
        Assert.Equal("payment_unknown_order", (await _h.Bookings.Db.GatewayEvents.SingleAsync()).Error);
    }

    [Fact]
    public async Task A_payment_that_is_not_captured_does_not_confirm_anything()
    {
        var (booking, orderId) = await OpenPaymentAsync();

        var result = await SendAsync(Captured(orderId, "pay_1", 100_000, status: "authorized"), NextEventId());

        Assert.Equal(WebhookOutcome.Ignored, result.Value!.Outcome);
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task A_payment_in_another_currency_is_not_applied()
    {
        var (booking, orderId) = await OpenPaymentAsync();
        var json = Captured(orderId, "pay_1", 100_000).Replace("\"INR\"", "\"USD\"");

        var result = await SendAsync(json, NextEventId());

        Assert.Equal(WebhookOutcome.NeedsAttention, result.Value!.Outcome);
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task A_failed_payment_try_is_recorded_but_the_customer_can_still_pay_on_the_same_order()
    {
        var (booking, orderId) = await OpenPaymentAsync();
        var failed = Event("payment.failed", new { payment = Entity(new { id = "pay_bad", order_id = orderId, amount = 100000, currency = "INR", status = "failed", error_code = "BAD_REQUEST_ERROR" }) });

        var first = await SendAsync(failed, NextEventId());
        Assert.Equal(WebhookOutcome.Processed, first.Value!.Outcome);
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);

        var paid = await SendAsync(Captured(orderId, "pay_good", 100_000), NextEventId());

        Assert.Equal(WebhookOutcome.Processed, paid.Value!.Outcome);
        Assert.Equal(BookingStatus.Confirmed, (await _h.BookingAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task Event_types_we_do_not_act_on_are_acknowledged_and_ignored()
    {
        var result = await SendAsync("""{"event":"payment.authorized","payload":{}}""", NextEventId());

        Assert.Equal(WebhookOutcome.Ignored, result.Value!.Outcome);
        Assert.NotNull((await _h.Bookings.Db.GatewayEvents.SingleAsync()).ProcessedAt);
    }

    // --- The cancellation / payment race, through the webhook ---

    [Fact]
    public async Task A_captured_payment_for_a_booking_the_customer_cancelled_is_refunded_by_the_webhook()
    {
        var (booking, orderId) = await OpenPaymentAsync();
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);

        var result = await SendAsync(Captured(orderId, "pay_1", 100_000), NextEventId());

        Assert.Equal(WebhookOutcome.Processed, result.Value!.Outcome);
        Assert.Equal(BookingStatus.RefundPending, (await _h.BookingAsync(booking.Id)).Status);
        var sent = Assert.Single(_h.Gateway.RefundRequests);
        Assert.Equal("pay_1", sent.PaymentId);
        Assert.Equal(100_000, sent.AmountPaise);
    }

    // --- Refund results ---

    [Fact]
    public async Task A_processed_refund_marks_the_cancelled_booking_refunded()
    {
        var (booking, orderId) = await OpenPaymentAsync();
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        await SendAsync(Captured(orderId, "pay_1", 100_000), NextEventId());
        var refund = await _h.Bookings.Db.Refunds.SingleAsync();
        var json = RefundEvent("refund.processed", refund.ProviderRefundId!);

        var result = await SendAsync(json, NextEventId());

        Assert.Equal(WebhookOutcome.Processed, result.Value!.Outcome);
        Assert.Equal(BookingStatus.Refunded, (await _h.BookingAsync(booking.Id)).Status);
        var processed = await _h.Bookings.Db.Refunds.SingleAsync();
        Assert.Equal(RefundStatus.Processed, processed.Status);
        Assert.NotNull(processed.ProcessedAt);
    }

    [Fact]
    public async Task A_processed_refund_for_an_expired_booking_leaves_the_booking_expired()
    {
        var (booking, orderId) = await OpenPaymentAsync();
        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(31));
        await _h.Bookings.Expiry.ExpireDueHoldsAsync(None);
        await SendAsync(Captured(orderId, "pay_1", 100_000), NextEventId());
        var refund = await _h.Bookings.Db.Refunds.SingleAsync();

        await SendAsync(RefundEvent("refund.processed", refund.ProviderRefundId!), NextEventId());

        Assert.Equal(BookingStatus.Expired, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal(RefundStatus.Processed, (await _h.Bookings.Db.Refunds.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_refund_made_outside_the_system_is_recorded_for_a_person_to_look_at()
    {
        var result = await SendAsync("""{"event":"refund.processed","payload":{"refund":{"entity":{"id":"rfnd_from_dashboard"}}}}""", NextEventId());

        Assert.Equal(WebhookOutcome.NeedsAttention, result.Value!.Outcome);
        Assert.Equal("payment_unknown_refund", (await _h.Bookings.Db.GatewayEvents.SingleAsync()).Error);
    }

    [Fact]
    public async Task A_failed_refund_is_marked_failed_and_the_booking_keeps_waiting()
    {
        var (booking, orderId) = await OpenPaymentAsync();
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        await SendAsync(Captured(orderId, "pay_1", 100_000), NextEventId());
        var refund = await _h.Bookings.Db.Refunds.SingleAsync();

        await SendAsync(RefundEvent("refund.failed", refund.ProviderRefundId!), NextEventId());

        Assert.Equal(RefundStatus.Failed, (await _h.Bookings.Db.Refunds.SingleAsync()).Status);
        Assert.Equal(BookingStatus.RefundPending, (await _h.BookingAsync(booking.Id)).Status);
    }

    // --- Privacy ---

    [Fact]
    public async Task Only_identifiers_amounts_and_statuses_are_stored_never_contact_or_card_details()
    {
        var (_, orderId) = await OpenPaymentAsync();

        await SendAsync(Captured(orderId, "pay_1", 100_000), NextEventId());

        var stored = (await _h.Bookings.Db.GatewayEvents.SingleAsync()).Payload;
        Assert.Contains(orderId, stored);
        Assert.Contains("pay_1", stored);
        Assert.Contains("100000", stored);
        Assert.DoesNotContain("customer@example.com", stored);
        Assert.DoesNotContain("9876543210", stored);
        Assert.DoesNotContain("someone@okbank", stored);
        Assert.DoesNotContain("4242", stored);
        Assert.DoesNotContain("Visa", stored);
    }
}
