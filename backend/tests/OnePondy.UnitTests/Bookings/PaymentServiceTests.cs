using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Bookings;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>Phase 7 step 7.2: starting a payment, and what happens when the result arrives after the booking moved on.</summary>
public class PaymentServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    // --- Starting a payment ---

    [Fact]
    public async Task Starting_a_payment_creates_a_gateway_order_for_the_booking_total()
    {
        var booking = await _h.AwaitingPaymentAsync();

        var started = await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.True(started.IsSuccess);
        Assert.Equal("order_test_1", started.Value.OrderId);
        Assert.Equal(1000m, started.Value.Amount);
        Assert.Equal(100_000, started.Value.AmountPaise);
        Assert.Equal("INR", started.Value.Currency);
        Assert.Equal("razorpay", started.Value.Provider);
        Assert.Equal("sandbox", started.Value.Mode);
        Assert.Equal(PaymentHarness.KeyId, started.Value.KeyId);
        Assert.Equal(booking.ExpiresAt, started.Value.PaymentExpiresAt);

        var request = Assert.Single(_h.Gateway.OrderRequests);
        Assert.Equal(100_000, request.AmountPaise);
        Assert.Equal(booking.Id, request.BookingId);

        var attempt = await _h.Bookings.Db.PaymentAttempts.SingleAsync();
        Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
        Assert.Equal("order_test_1", attempt.ProviderOrderId);
        Assert.Equal(1000m, attempt.Amount);
    }

    [Fact]
    public async Task Asking_twice_returns_the_same_order_and_only_one_is_created_at_the_gateway()
    {
        var booking = await _h.AwaitingPaymentAsync();

        var first = await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);
        var second = await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Equal(first.Value!.OrderId, second.Value!.OrderId);
        Assert.Equal(first.Value.AttemptId, second.Value.AttemptId);
        Assert.Single(_h.Gateway.OrderRequests);
        Assert.Equal(1, await _h.Bookings.Db.PaymentAttempts.CountAsync());
    }

    [Fact]
    public async Task After_a_failed_attempt_the_customer_can_try_again_with_a_new_order()
    {
        var booking = await _h.AwaitingPaymentAsync();
        await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);
        (await _h.Bookings.Db.PaymentAttempts.SingleAsync()).MarkFailed(_h.Bookings.Time.GetUtcNow());
        await _h.Bookings.Db.SaveChangesAsync();

        var retry = await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Equal("order_test_2", retry.Value!.OrderId);
        Assert.Equal(2, await _h.Bookings.Db.PaymentAttempts.CountAsync());
    }

    [Fact]
    public async Task Only_the_booking_customer_can_start_its_payment()
    {
        var booking = await _h.AwaitingPaymentAsync();

        var stranger = await _h.Payments.StartPaymentAsync(booking.Id, Guid.NewGuid(), None);
        var partner = await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Owner, None);
        var unknown = await _h.Payments.StartPaymentAsync(Guid.NewGuid(), _h.Bookings.Customer, None);

        Assert.Equal("booking_not_found", stranger.Error!.Code);
        Assert.Equal("booking_not_found", partner.Error!.Code);
        Assert.Equal("booking_not_found", unknown.Error!.Code);
        Assert.Empty(_h.Gateway.OrderRequests);
    }

    [Fact]
    public async Task A_booking_the_partner_has_not_accepted_yet_is_not_due_for_payment()
    {
        await _h.Bookings.EnablePaymentsAsync();
        var pending = (await _h.Bookings.Bookings.CreateAsync(_h.Bookings.Customer, _h.Bookings.Input(), None)).Value!;

        var result = await _h.Payments.StartPaymentAsync(pending.Id, _h.Bookings.Customer, None);

        Assert.Equal("payment_not_due", result.Error!.Code);
        Assert.Empty(_h.Gateway.OrderRequests);
    }

    [Fact]
    public async Task A_booking_that_is_cancelled_or_confirmed_is_not_due_for_payment()
    {
        var cancelled = await _h.AwaitingPaymentAsync("idem-a");
        await _h.Bookings.Bookings.CancelMineAsync(cancelled.Id, _h.Bookings.Customer, null, cancelled.Version, None);

        var result = await _h.Payments.StartPaymentAsync(cancelled.Id, _h.Bookings.Customer, None);

        Assert.Equal("payment_not_due", result.Error!.Code);
    }

    [Fact]
    public async Task Once_the_payment_window_has_closed_a_new_order_is_refused()
    {
        var booking = await _h.AwaitingPaymentAsync();
        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(31));

        var result = await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Equal("payment_window_closed", result.Error!.Code);
        Assert.Empty(_h.Gateway.OrderRequests);
    }

    [Fact]
    public async Task When_the_gateway_is_unavailable_no_attempt_is_recorded_and_the_customer_can_retry()
    {
        var booking = await _h.AwaitingPaymentAsync();
        _h.Gateway.Unavailable = true;

        var failed = await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Equal("payments_unavailable", failed.Error!.Code);
        Assert.Equal(0, await _h.Bookings.Db.PaymentAttempts.CountAsync());

        _h.Gateway.Unavailable = false;
        var retry = await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);
        Assert.True(retry.IsSuccess);
    }

    // --- A payment arrives ---

    [Fact]
    public async Task A_payment_for_a_booking_waiting_on_it_confirms_the_booking()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;

        var captured = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);

        Assert.Equal(CaptureOutcome.Confirmed, captured.Value!.Outcome);
        var detail = await _h.BookingAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, detail.Status);
        Assert.Null(detail.ExpiresAt);
        Assert.Equal(BookingActorType.System, detail.History[^1].ActorType);
        Assert.Equal(2, await _h.ReservedAsync());

        var attempt = await _h.Bookings.Db.PaymentAttempts.SingleAsync();
        Assert.Equal(PaymentAttemptStatus.Captured, attempt.Status);
        Assert.Equal("pay_1", attempt.ProviderPaymentId);
        Assert.Empty(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task The_same_payment_reported_twice_changes_nothing_the_second_time()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);
        var historyBefore = (await _h.BookingAsync(booking.Id)).History.Count;

        var again = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);

        Assert.Equal(CaptureOutcome.AlreadyRecorded, again.Value!.Outcome);
        Assert.Equal(historyBefore, (await _h.BookingAsync(booking.Id)).History.Count);
        Assert.Empty(_h.Gateway.RefundRequests);
        Assert.Equal(0, await _h.Bookings.Db.Refunds.CountAsync());
    }

    [Fact]
    public async Task A_payment_for_an_unknown_order_is_refused()
    {
        var result = await _h.Payments.RecordCaptureAsync("order_nobody_knows", "pay_1", 1000m, None);

        Assert.Equal("payment_unknown_order", result.Error!.Code);
    }

    [Fact]
    public async Task A_payment_for_the_wrong_amount_is_not_applied()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;

        var result = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 999m, None);

        Assert.Equal("payment_amount_mismatch", result.Error!.Code);
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal(PaymentAttemptStatus.Created, (await _h.Bookings.Db.PaymentAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_different_payment_reported_for_an_already_captured_order_needs_a_manual_check()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);

        var second = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_2", 1000m, None);

        Assert.Equal("payment_already_captured", second.Error!.Code);
    }

    // --- Cancellation and payment racing ---

    [Fact]
    public async Task A_payment_that_lands_after_the_customer_cancelled_is_refunded_in_full()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, "Changed my mind", booking.Version, None);
        Assert.Equal(0, await _h.ReservedAsync());

        var captured = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);

        Assert.Equal(CaptureOutcome.RefundRaised, captured.Value!.Outcome);
        var detail = await _h.BookingAsync(booking.Id);
        Assert.Equal(BookingStatus.RefundPending, detail.Status);
        Assert.Equal(BookingActorType.System, detail.History[^1].ActorType);
        Assert.Equal(0, await _h.ReservedAsync());

        var refund = await _h.Bookings.Db.Refunds.SingleAsync();
        Assert.Equal(captured.Value.RefundId, refund.Id);
        Assert.Equal(1000m, refund.Amount);
        Assert.Equal(RefundReason.LatePayment, refund.Reason);
        Assert.Equal(RefundStatus.Pending, refund.Status);
        Assert.Equal("rfnd_test_1", refund.ProviderRefundId);

        var sent = Assert.Single(_h.Gateway.RefundRequests);
        Assert.Equal("pay_1", sent.PaymentId);
        Assert.Equal(100_000, sent.AmountPaise);
        Assert.Equal(PaymentAttemptStatus.Captured, (await _h.Bookings.Db.PaymentAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_payment_that_lands_after_the_hold_expired_is_refunded_and_the_booking_stays_expired()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(31));
        await new BookingHoldExpiry(_h.Bookings.Db, _h.Bookings.Bookings, _h.Bookings.Time).ExpireDueHoldsAsync(None);

        var captured = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);

        Assert.Equal(CaptureOutcome.RefundRaised, captured.Value!.Outcome);
        Assert.Equal(BookingStatus.Expired, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal(0, await _h.ReservedAsync());
        Assert.Equal(1000m, (await _h.Bookings.Db.Refunds.SingleAsync()).Amount);
        Assert.Single(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task A_payment_that_lands_after_the_partner_rejected_is_refunded()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Bookings.Bookings.RejectAsync(_h.Bookings.BusinessId, booking.Id, booking.Version, _h.Bookings.Owner, "Cannot host", None);

        var captured = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);

        Assert.Equal(CaptureOutcome.RefundRaised, captured.Value!.Outcome);
        Assert.Equal(BookingStatus.RefundPending, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Single(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task A_second_payment_for_a_booking_that_is_already_paid_is_refunded_and_the_booking_is_left_alone()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var first = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Payments.RecordCaptureAsync(first.OrderId, "pay_1", 1000m, None);
        // The customer somehow ends up with a second open order (for example two devices), and pays on both.
        var second = PaymentAttempt.Create(booking.Id, "order_second", 1000m, "INR", _h.Bookings.Time.GetUtcNow());
        _h.Bookings.Db.PaymentAttempts.Add(second);
        await _h.Bookings.Db.SaveChangesAsync();

        var captured = await _h.Payments.RecordCaptureAsync("order_second", "pay_2", 1000m, None);

        Assert.Equal(CaptureOutcome.RefundRaised, captured.Value!.Outcome);
        Assert.Equal(BookingStatus.Confirmed, (await _h.BookingAsync(booking.Id)).Status);
        var refund = await _h.Bookings.Db.Refunds.SingleAsync();
        Assert.Equal(second.Id, refund.PaymentAttemptId);
        Assert.Equal("pay_2", Assert.Single(_h.Gateway.RefundRequests).PaymentId);
    }

    [Fact]
    public async Task If_the_gateway_cannot_take_the_refund_yet_it_stays_pending_and_a_repeat_delivery_sends_it()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        _h.Gateway.RefundsFail = true;

        var captured = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);

        Assert.Equal(CaptureOutcome.RefundRaised, captured.Value!.Outcome);
        var refund = await _h.Bookings.Db.Refunds.SingleAsync();
        Assert.Null(refund.ProviderRefundId);
        Assert.Equal(RefundStatus.Pending, refund.Status);
        Assert.Empty(_h.Gateway.RefundRequests);

        _h.Gateway.RefundsFail = false;
        var repeat = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);

        Assert.Equal(CaptureOutcome.AlreadyRecorded, repeat.Value!.Outcome);
        Assert.Equal("rfnd_test_1", (await _h.Bookings.Db.Refunds.SingleAsync()).ProviderRefundId);
        Assert.Single(_h.Gateway.RefundRequests);
        Assert.Equal(1, await _h.Bookings.Db.Refunds.CountAsync());
    }

    [Fact]
    public async Task A_refund_is_only_ever_raised_once_for_a_payment()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);

        await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);
        await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);
        await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_1", 1000m, None);

        Assert.Equal(1, await _h.Bookings.Db.Refunds.CountAsync());
        Assert.Single(_h.Gateway.RefundRequests);
    }
}
