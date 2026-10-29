using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Bookings;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>
/// Phase 7 step 7.4: before a booking that was waiting for payment is released, the Worker asks the gateway whether
/// it was paid, so a payment whose webhook is late or lost is not thrown away.
/// </summary>
public class PaymentExpiryTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    private async Task<(BookingDetail Booking, string OrderId)> UnpaidAsync()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        return (booking, started.OrderId);
    }

    private void PastThePaymentWindow() => _h.Bookings.Time.Advance(TimeSpan.FromMinutes(31));

    private Task<HoldExpiryResult> RunWorkerAsync() => _h.Bookings.Expiry.ExpireDueHoldsAsync(None);

    private async Task<PaymentAttempt> AttemptAsync() => await _h.Bookings.Db.PaymentAttempts.AsNoTracking().SingleAsync();

    // --- Not paid: released as before, and the order is closed ---

    [Fact]
    public async Task A_booking_nobody_paid_for_is_expired_its_seats_are_freed_and_its_order_closed()
    {
        var (booking, _) = await UnpaidAsync();
        PastThePaymentWindow();

        var result = await RunWorkerAsync();

        Assert.Equal(new HoldExpiryResult(1, 0, 0), result);
        Assert.Equal(BookingStatus.Expired, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal(0, await _h.ReservedAsync());
        Assert.Equal(PaymentAttemptStatus.Expired, (await AttemptAsync()).Status);
        Assert.Equal(1, _h.Gateway.Lookups);
    }

    [Fact]
    public async Task Payments_the_gateway_did_not_capture_do_not_save_a_booking()
    {
        var (booking, orderId) = await UnpaidAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_failed", 100_000, status: "failed");
        _h.Gateway.CustomerPaid(orderId, "pay_auth", 100_000, status: "authorized");
        PastThePaymentWindow();

        var result = await RunWorkerAsync();

        Assert.Equal(1, result.Expired);
        Assert.Equal(BookingStatus.Expired, (await _h.BookingAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task A_booking_nobody_started_paying_for_expires_without_asking_the_gateway()
    {
        var booking = await _h.AwaitingPaymentAsync();
        PastThePaymentWindow();

        var result = await RunWorkerAsync();

        Assert.Equal(1, result.Expired);
        Assert.Equal(BookingStatus.Expired, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal(0, _h.Gateway.Lookups);
    }

    [Fact]
    public async Task A_request_the_partner_never_answered_expires_without_asking_the_gateway()
    {
        await _h.Bookings.EnablePaymentsAsync();
        var pending = (await _h.Bookings.Bookings.CreateAsync(_h.Bookings.Customer, _h.Bookings.Input(), None)).Value!;
        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(31));

        var result = await RunWorkerAsync();

        Assert.Equal(1, result.Expired);
        Assert.Equal(BookingStatus.Expired, (await _h.BookingAsync(pending.Id)).Status);
        Assert.Equal(0, _h.Gateway.Lookups);
    }

    // --- Paid, but we were never told ---

    [Fact]
    public async Task A_payment_the_webhook_never_reported_is_found_at_the_gateway_and_the_booking_is_kept()
    {
        var (booking, orderId) = await UnpaidAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_lost_webhook", 100_000);
        PastThePaymentWindow();

        var result = await RunWorkerAsync();

        Assert.Equal(new HoldExpiryResult(0, 1, 0), result);
        var detail = await _h.BookingAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, detail.Status);
        Assert.Equal(BookingActorType.System, detail.History[^1].ActorType);
        Assert.Equal(2, await _h.ReservedAsync());
        var attempt = await AttemptAsync();
        Assert.Equal(PaymentAttemptStatus.Captured, attempt.Status);
        Assert.Equal("pay_lost_webhook", attempt.ProviderPaymentId);
        Assert.Empty(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task The_late_webhook_for_a_payment_the_worker_already_applied_changes_nothing()
    {
        var (booking, orderId) = await UnpaidAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_1", 100_000);
        PastThePaymentWindow();
        await RunWorkerAsync();
        var history = (await _h.BookingAsync(booking.Id)).History.Count;

        var webhook = await _h.Payments.RecordCaptureAsync(orderId, "pay_1", 1000m, None);

        Assert.Equal(CaptureOutcome.AlreadyRecorded, webhook.Value!.Outcome);
        Assert.Equal(history, (await _h.BookingAsync(booking.Id)).History.Count);
        Assert.Empty(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task Running_the_worker_again_after_a_booking_was_saved_does_nothing_more()
    {
        var (booking, orderId) = await UnpaidAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_1", 100_000);
        PastThePaymentWindow();
        await RunWorkerAsync();

        var second = await RunWorkerAsync();

        Assert.Equal(new HoldExpiryResult(0, 0, 0), second);
        Assert.Equal(BookingStatus.Confirmed, (await _h.BookingAsync(booking.Id)).Status);
    }

    // --- The gateway cannot be asked ---

    [Fact]
    public async Task When_the_gateway_cannot_be_asked_the_hold_is_kept_for_a_while_and_retried()
    {
        var (booking, _) = await UnpaidAsync();
        _h.Gateway.LookupsFail = true;
        PastThePaymentWindow();

        var first = await RunWorkerAsync();

        Assert.Equal(new HoldExpiryResult(0, 0, 1), first);
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal(2, await _h.ReservedAsync());

        // The gateway is back, and the customer had in fact paid: the booking is saved.
        _h.Gateway.LookupsFail = false;
        _h.Gateway.CustomerPaid((await AttemptAsync()).ProviderOrderId, "pay_late_found", 100_000);
        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(1));

        var second = await RunWorkerAsync();

        Assert.Equal(new HoldExpiryResult(0, 1, 0), second);
        Assert.Equal(BookingStatus.Confirmed, (await _h.BookingAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task If_the_gateway_stays_unreachable_the_hold_is_released_after_the_grace_period_and_a_later_payment_is_refunded()
    {
        var (booking, orderId) = await UnpaidAsync();
        _h.Gateway.LookupsFail = true;
        PastThePaymentWindow();
        Assert.Equal(1, (await RunWorkerAsync()).Deferred);

        _h.Bookings.Time.Advance(_h.Bookings.Payments.GatewayCheckGrace);
        var afterGrace = await RunWorkerAsync();

        Assert.Equal(1, afterGrace.Expired);
        Assert.Equal(BookingStatus.Expired, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal(0, await _h.ReservedAsync());

        // The payment turns up afterwards: nothing is lost, it goes back in full.
        var late = await _h.Payments.RecordCaptureAsync(orderId, "pay_after", 1000m, None);

        Assert.Equal(CaptureOutcome.RefundRaised, late.Value!.Outcome);
        Assert.Equal(BookingStatus.Expired, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal("pay_after", Assert.Single(_h.Gateway.RefundRequests).PaymentId);
    }

    [Fact]
    public async Task A_payment_the_gateway_reports_for_the_wrong_amount_is_not_confirmed_and_not_silently_expired_at_once()
    {
        var (booking, orderId) = await UnpaidAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_wrong_amount", 99_999);
        PastThePaymentWindow();

        var result = await RunWorkerAsync();

        Assert.Equal(new HoldExpiryResult(0, 0, 1), result);
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);
    }

    // --- Orders are closed when a booking ends any other way ---

    [Fact]
    public async Task Cancelling_closes_the_open_order_and_a_payment_that_still_lands_is_refunded()
    {
        var (booking, orderId) = await UnpaidAsync();

        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);

        Assert.Equal(PaymentAttemptStatus.Expired, (await AttemptAsync()).Status);
        var late = await _h.Payments.RecordCaptureAsync(orderId, "pay_1", 1000m, None);
        Assert.Equal(CaptureOutcome.RefundRaised, late.Value!.Outcome);
        Assert.Equal(PaymentAttemptStatus.Captured, (await AttemptAsync()).Status);
    }

    [Fact]
    public async Task Rejecting_closes_the_open_order_too()
    {
        var (booking, _) = await UnpaidAsync();

        await _h.Bookings.Bookings.RejectAsync(_h.Bookings.BusinessId, booking.Id, booking.Version, _h.Bookings.Owner, "Cannot host", None);

        Assert.Equal(PaymentAttemptStatus.Expired, (await AttemptAsync()).Status);
    }

    [Fact]
    public async Task A_confirmed_bookings_paid_attempt_is_left_alone_when_it_is_later_cancelled()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, orderId) = await UnpaidAsync();
        await _h.Payments.RecordCaptureAsync(orderId, "pay_1", 1000m, None);
        var confirmed = await _h.BookingAsync(booking.Id);

        var cancelled = await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, confirmed.Version, None);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(PaymentAttemptStatus.Captured, (await AttemptAsync()).Status);
    }

    [Fact]
    public async Task After_the_order_is_closed_a_new_payment_cannot_be_started()
    {
        var (booking, _) = await UnpaidAsync();
        PastThePaymentWindow();
        await RunWorkerAsync();

        var retry = await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Equal("payment_not_due", retry.Error!.Code);
    }
}
