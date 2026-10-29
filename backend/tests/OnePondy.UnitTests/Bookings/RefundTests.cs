using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Bookings;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>Phase 7 step 7.5: what a cancellation refunds (spec D22), and that it is gated until the policy is approved.</summary>
public class RefundTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    private Task<Application.Common.Result<BookingDetail>> CustomerCancelsAsync(BookingDetail booking) =>
        _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, "Changed my mind", booking.Version, None);

    private Task<Application.Common.Result<BookingDetail>> PartnerCancelsAsync(BookingDetail booking) =>
        _h.Bookings.Bookings.CancelForBusinessAsync(_h.Bookings.BusinessId, booking.Id, booking.Version, _h.Bookings.Owner, "Closed that day", None);

    private async Task<Refund> OnlyRefundAsync() => await _h.Bookings.Db.Refunds.SingleAsync();

    // --- Who gets what ---

    [Fact]
    public async Task A_customer_cancelling_before_the_free_deadline_is_refunded_in_full()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, paymentId) = await _h.PaidAsync();

        var cancelled = await CustomerCancelsAsync(booking);

        Assert.Equal(BookingStatus.RefundPending, cancelled.Value!.Status);
        var refund = await OnlyRefundAsync();
        Assert.Equal(1000m, refund.Amount);
        Assert.Equal(RefundReason.CustomerCancelled, refund.Reason);
        Assert.Equal(RefundStatus.Pending, refund.Status);
        Assert.Equal("rfnd_test_1", refund.ProviderRefundId);
        var sent = Assert.Single(_h.Gateway.RefundRequests);
        Assert.Equal(paymentId, sent.PaymentId);
        Assert.Equal(100_000, sent.AmountPaise);
        Assert.Equal(refund.Id.ToString("N"), sent.Receipt);
    }

    [Fact]
    public async Task A_customer_cancelling_after_the_deadline_gets_the_listings_refund_percentage()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        _h.Bookings.Time.Advance(TimeSpan.FromHours(2));

        await CustomerCancelsAsync(booking);

        Assert.Equal(500m, (await OnlyRefundAsync()).Amount);
        Assert.Equal(50_000, Assert.Single(_h.Gateway.RefundRequests).AmountPaise);
    }

    [Fact]
    public async Task A_partner_cancelling_always_refunds_everything_even_after_the_deadline()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        _h.Bookings.Time.Advance(TimeSpan.FromHours(23));

        var cancelled = await PartnerCancelsAsync(booking);

        Assert.Equal(BookingStatus.RefundPending, cancelled.Value!.Status);
        var refund = await OnlyRefundAsync();
        Assert.Equal(1000m, refund.Amount);
        Assert.Equal(RefundReason.PartnerCancelled, refund.Reason);
    }

    [Fact]
    public async Task A_policy_that_refunds_nothing_leaves_the_booking_cancelled_with_no_refund_and_no_gateway_call()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        await _h.Bookings.EnablePaymentsAsync();
        var input = await _h.Bookings.NewListingInputAsync(500m, "idem-zero", refundPercentage: 0);
        var created = (await _h.Bookings.Bookings.CreateAsync(_h.Bookings.Customer, input, None)).Value!;
        var accepted = (await _h.Bookings.Bookings.AcceptAsync(_h.Bookings.BusinessId, created.Id, created.Version, _h.Bookings.Owner, None)).Value!;
        var started = (await _h.Payments.StartPaymentAsync(accepted.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_zero", 1000m, None);
        var paid = (await _h.Bookings.Bookings.GetMineAsync(accepted.Id, _h.Bookings.Customer, None)).Value!;
        _h.Bookings.Time.Advance(TimeSpan.FromDays(2) - TimeSpan.FromHours(1)); // inside the 24-hour no-free-cancellation window

        var cancelled = await CustomerCancelsAsync(paid);

        Assert.Equal(BookingStatus.Cancelled, cancelled.Value!.Status);
        Assert.Equal(0, await _h.Bookings.Db.Refunds.CountAsync());
        Assert.Empty(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task The_cancellation_releases_the_seats_and_the_history_shows_both_steps()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        Assert.Equal(2, await _h.ReservedAsync());

        var cancelled = await CustomerCancelsAsync(booking);

        Assert.Equal(0, await _h.ReservedAsync());
        var steps = cancelled.Value!.History.TakeLast(2).ToList();
        Assert.Equal((BookingStatus.Confirmed, BookingStatus.Cancelled, BookingActorType.Customer), (steps[0].PreviousStatus!.Value, steps[0].NewStatus, steps[0].ActorType));
        Assert.Equal((BookingStatus.Cancelled, BookingStatus.RefundPending, BookingActorType.System), (steps[1].PreviousStatus!.Value, steps[1].NewStatus, steps[1].ActorType));
    }

    [Fact]
    public async Task Cancelling_twice_refunds_once()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        await CustomerCancelsAsync(booking);

        var again = await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, (await _h.BookingAsync(booking.Id)).Version, None);

        Assert.Equal("booking_invalid_transition", again.Error!.Code);
        Assert.Equal(1, await _h.Bookings.Db.Refunds.CountAsync());
        Assert.Single(_h.Gateway.RefundRequests);
    }

    // --- Switched off until the policy is approved ---

    [Fact]
    public async Task While_refunds_are_off_a_paid_booking_cannot_be_cancelled_and_nothing_changes()
    {
        var (booking, _, _) = await _h.PaidAsync();
        var seatsBefore = await _h.ReservedAsync();

        var byCustomer = await CustomerCancelsAsync(booking);
        var byPartner = await PartnerCancelsAsync(booking);

        Assert.Equal("refunds_not_enabled", byCustomer.Error!.Code);
        Assert.Equal("refunds_not_enabled", byPartner.Error!.Code);
        var after = await _h.BookingAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, after.Status);
        Assert.Equal(booking.History.Count, after.History.Count);
        Assert.Equal(seatsBefore, await _h.ReservedAsync());
        Assert.Equal(0, await _h.Bookings.Db.Refunds.CountAsync());
        Assert.Empty(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task A_refused_cancellation_leaves_nothing_half_done_so_it_works_once_refunds_are_on()
    {
        var (booking, _, _) = await _h.PaidAsync();
        await CustomerCancelsAsync(booking);

        _h.Bookings.Payments.RefundsEnabled = true;
        var retry = await CustomerCancelsAsync(await _h.BookingAsync(booking.Id));

        Assert.Equal(BookingStatus.RefundPending, retry.Value!.Status);
        Assert.Equal(1, await _h.Bookings.Db.Refunds.CountAsync());
    }

    [Fact]
    public async Task A_free_booking_can_be_cancelled_whether_or_not_refunds_are_on()
    {
        await _h.Bookings.EnablePaymentsAsync();
        var free = await _h.Bookings.NewListingInputAsync(0m, "idem-free");
        var created = (await _h.Bookings.Bookings.CreateAsync(_h.Bookings.Customer, free, None)).Value!;
        var confirmed = (await _h.Bookings.Bookings.AcceptAsync(_h.Bookings.BusinessId, created.Id, created.Version, _h.Bookings.Owner, None)).Value!;

        var cancelled = await CustomerCancelsAsync(confirmed);

        Assert.Equal(BookingStatus.Cancelled, cancelled.Value!.Status);
        Assert.Equal(0, await _h.Bookings.Db.Refunds.CountAsync());
    }

    [Fact]
    public async Task Cancelling_a_booking_that_was_not_paid_for_yet_is_not_blocked_by_refunds_being_off()
    {
        var awaiting = await _h.AwaitingPaymentAsync();

        var cancelled = await CustomerCancelsAsync(awaiting);

        Assert.Equal(BookingStatus.Cancelled, cancelled.Value!.Status);
    }

    [Fact]
    public async Task A_refund_for_a_late_payment_is_not_held_back_by_the_switch()
    {
        // These refund money OnePondy should never have kept, so they do not wait for the cancellation policy.
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await CustomerCancelsAsync(booking);

        var late = await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_late", 1000m, None);

        Assert.Equal(CaptureOutcome.RefundRaised, late.Value!.Outcome);
        Assert.Single(_h.Gateway.RefundRequests);
    }

    // --- Refunds that do not reach the gateway ---

    [Fact]
    public async Task A_refund_the_gateway_could_not_take_is_kept_and_sent_by_the_sweep_once_it_is_a_little_old()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        _h.Gateway.RefundsFail = true;

        var cancelled = await CustomerCancelsAsync(booking);

        Assert.Equal(BookingStatus.RefundPending, cancelled.Value!.Status);
        Assert.Null((await OnlyRefundAsync()).ProviderRefundId);
        _h.Gateway.RefundsFail = false;

        // Too fresh: the request that made it may still be about to send it.
        Assert.Equal(0, await _h.Bookings.Refunds.SendUnsentAsync(50, None));

        _h.Bookings.Time.Advance(RefundService.SweepAge + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await _h.Bookings.Refunds.SendUnsentAsync(50, None));
        Assert.NotNull((await OnlyRefundAsync()).ProviderRefundId);
        Assert.Single(_h.Gateway.RefundRequests);

        // Nothing left to send, so a second pass does nothing.
        Assert.Equal(0, await _h.Bookings.Refunds.SendUnsentAsync(50, None));
        Assert.Single(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task A_sweep_that_still_cannot_reach_the_gateway_leaves_the_refund_for_the_next_pass()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        _h.Gateway.RefundsFail = true;
        await CustomerCancelsAsync(booking);
        _h.Bookings.Time.Advance(RefundService.SweepAge + TimeSpan.FromSeconds(1));

        Assert.Equal(0, await _h.Bookings.Refunds.SendUnsentAsync(50, None));

        Assert.Null((await OnlyRefundAsync()).ProviderRefundId);
        Assert.Equal(RefundStatus.Pending, (await OnlyRefundAsync()).Status);
    }

    // --- The refund completes ---

    [Fact]
    public async Task When_the_gateway_reports_the_refund_paid_the_booking_becomes_refunded()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        await CustomerCancelsAsync(booking);

        var done = await _h.Payments.RecordRefundProcessedAsync((await OnlyRefundAsync()).ProviderRefundId!, None);

        Assert.True(done.IsSuccess);
        Assert.Equal(BookingStatus.Refunded, (await _h.BookingAsync(booking.Id)).Status);
        Assert.Equal(RefundStatus.Processed, (await OnlyRefundAsync()).Status);
    }

    // --- The check on the database's rule ---

    [Fact]
    public async Task The_refund_is_never_more_than_was_paid()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();

        await CustomerCancelsAsync(booking);

        var paid = (await _h.Bookings.Db.PaymentAttempts.SingleAsync()).Amount;
        Assert.True((await OnlyRefundAsync()).Amount <= paid);
    }
}
