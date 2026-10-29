using OnePondy.Application.Bookings;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>Phase 7 step 7.8: what the app shows after paying (receipt, refund progress) and the "is it paid yet?" check.</summary>
public class ReceiptAndCheckNowTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    private Task<BookingDetail> DetailAsync(Guid bookingId) => _h.BookingAsync(bookingId);

    // --- Receipt and refund progress ---

    [Fact]
    public async Task A_booking_nobody_has_paid_for_has_no_receipt_and_no_refunds()
    {
        var booking = await _h.AwaitingPaymentAsync();

        var detail = await DetailAsync(booking.Id);

        Assert.Null(detail.Payment);
        Assert.Empty(detail.Refunds);
    }

    [Fact]
    public async Task A_paid_booking_carries_a_receipt_with_the_amount_the_time_and_the_gateways_reference()
    {
        var (booking, _, paymentId) = await _h.PaidAsync();

        var detail = await DetailAsync(booking.Id);

        var receipt = detail.Payment!;
        Assert.Equal((1000m, "INR", paymentId), (receipt.Amount, receipt.Currency, receipt.Reference));
        Assert.Equal(_h.Bookings.Time.GetUtcNow(), receipt.PaidAt);
        Assert.Empty(detail.Refunds);
    }

    [Fact]
    public async Task The_partner_sees_the_same_receipt_on_their_side_of_the_booking()
    {
        var (booking, _, _) = await _h.PaidAsync();

        var forPartner = (await _h.Bookings.Bookings.GetForBusinessAsync(_h.Bookings.BusinessId, booking.Id, _h.Bookings.Owner, None)).Value!;

        Assert.Equal(1000m, forPartner.Payment!.Amount);
    }

    [Fact]
    public async Task A_cancellation_shows_the_refund_pending_then_processed()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        _h.Bookings.Time.Advance(TimeSpan.FromHours(2)); // past the free deadline: half back
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);

        var pending = (await DetailAsync(booking.Id)).Refunds.Single();
        Assert.Equal((500m, RefundReason.CustomerCancelled, RefundStatus.Pending, null), (pending.Amount, pending.Reason, pending.Status, pending.ProcessedAt));

        await _h.Payments.RecordRefundProcessedAsync("rfnd_test_1", None);
        var done = (await DetailAsync(booking.Id)).Refunds.Single();
        Assert.Equal(RefundStatus.Processed, done.Status);
        Assert.NotNull(done.ProcessedAt);
        Assert.Equal(1000m, (await DetailAsync(booking.Id)).Payment!.Amount);
    }

    [Fact]
    public async Task A_payment_that_arrived_after_the_booking_lapsed_shows_what_was_paid_and_that_it_is_being_refunded()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_late", 1000m, None);

        var detail = await DetailAsync(booking.Id);

        Assert.Equal("pay_late", detail.Payment!.Reference);
        var refund = Assert.Single(detail.Refunds);
        Assert.Equal((1000m, RefundReason.LatePayment), (refund.Amount, refund.Reason));
    }

    // --- "Is it paid yet?" ---

    [Fact]
    public async Task Asking_after_checkout_finds_a_payment_the_webhook_has_not_reported_and_confirms_the_booking()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        _h.Gateway.CustomerPaid(started.OrderId, "pay_quick", 100_000);

        var error = await _h.Payments.CheckNowAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Null(error);
        var detail = await DetailAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, detail.Status);
        Assert.Equal("pay_quick", detail.Payment!.Reference);
    }

    [Fact]
    public async Task Asking_when_nothing_was_paid_changes_nothing()
    {
        var booking = await _h.AwaitingPaymentAsync();
        await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);

        var error = await _h.Payments.CheckNowAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Null(error);
        Assert.Equal(BookingStatus.AwaitingPayment, (await DetailAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task Someone_elses_booking_is_not_found_and_the_gateway_is_not_asked()
    {
        var booking = await _h.AwaitingPaymentAsync();
        await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None);
        var before = _h.Gateway.Lookups;

        var byStranger = await _h.Payments.CheckNowAsync(booking.Id, Guid.NewGuid(), None);
        var byPartner = await _h.Payments.CheckNowAsync(booking.Id, _h.Bookings.Owner, None);
        var unknown = await _h.Payments.CheckNowAsync(Guid.NewGuid(), _h.Bookings.Customer, None);

        Assert.Equal("booking_not_found", byStranger!.Code);
        Assert.Equal("booking_not_found", byPartner!.Code);
        Assert.Equal("booking_not_found", unknown!.Code);
        Assert.Equal(before, _h.Gateway.Lookups);
    }

    [Fact]
    public async Task A_booking_that_is_not_waiting_for_payment_is_not_checked_at_the_gateway()
    {
        var (booking, _, _) = await _h.PaidAsync();
        var before = _h.Gateway.Lookups;

        var error = await _h.Payments.CheckNowAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Null(error);
        Assert.Equal(before, _h.Gateway.Lookups);
    }

    [Fact]
    public async Task When_the_gateway_cannot_be_reached_the_check_still_answers_and_the_booking_waits()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        _h.Gateway.CustomerPaid(started.OrderId, "pay_quick", 100_000);
        _h.Gateway.LookupsFail = true;

        var error = await _h.Payments.CheckNowAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Null(error);
        Assert.Equal(BookingStatus.AwaitingPayment, (await DetailAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task Asking_twice_after_it_was_confirmed_does_not_book_it_twice()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        _h.Gateway.CustomerPaid(started.OrderId, "pay_quick", 100_000);

        await _h.Payments.CheckNowAsync(booking.Id, _h.Bookings.Customer, None);
        await _h.Payments.CheckNowAsync(booking.Id, _h.Bookings.Customer, None);

        Assert.Equal(3, (await _h.LedgerAsync()).Count);
        Assert.Empty(_h.Gateway.RefundRequests);
    }
}
