using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Bookings;
using OnePondy.Application.Food;
using OnePondy.Application.Payments;
using OnePondy.Domain.Food;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Food;

/// <summary>Phase 8 step 8.3: paying for food orders, refunding them, and settling them with restaurants, through the Phase 7 machinery.</summary>
public class OrderPaymentTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FoodOrderHarness _h = new();

    private Task<OrderDetail?> MineAsync(Guid id) => _h.MineAsync(id);

    // --- Starting a payment ---

    [Fact]
    public async Task A_payment_starts_once_the_restaurant_accepts_and_asking_twice_gives_the_same_gateway_order()
    {
        await _h.OpenAsync();
        var order = await _h.AwaitingPaymentAsync();

        var first = await _h.PaymentService.StartFoodOrderPaymentAsync(order.Id, _h.Buyer, None);
        var second = await _h.PaymentService.StartFoodOrderPaymentAsync(order.Id, _h.Buyer, None);

        Assert.True(first.IsSuccess, first.Error?.Detail);
        Assert.Equal(first.Value!.OrderId, second.Value!.OrderId);
        Assert.Equal(order.Id, first.Value.FoodOrderId);
        Assert.Equal(FoodOrderHarness.Total, first.Value.Amount);
        Assert.Equal(33925, first.Value.AmountPaise);
        Assert.Equal(FoodOrderHarness.KeyId, first.Value.KeyId);
        Assert.Equal(order.ExpiresAt, first.Value.PaymentExpiresAt);

        var request = Assert.Single(_h.Gateway.OrderRequests);
        Assert.Null(request.BookingId);
        Assert.Equal(order.Id, request.FoodOrderId);
        Assert.Equal("fo_" + order.Id.ToString("N"), request.Receipt);
        Assert.True(request.Receipt.Length <= 40);

        var attempt = Assert.Single(await _h.AttemptsAsync(order.Id));
        Assert.Null(attempt.BookingId);
    }

    [Fact]
    public async Task Only_the_customer_can_pay_and_only_while_the_order_is_waiting_for_it()
    {
        await _h.OpenAsync();
        var placed = await _h.PlacedAsync("not-yet");
        Assert.Equal("payment_not_due", (await _h.PaymentService.StartFoodOrderPaymentAsync(placed.Id, _h.Buyer, None)).Error!.Code);

        var order = await _h.AwaitingPaymentAsync();
        Assert.Equal("order_not_found", (await _h.PaymentService.StartFoodOrderPaymentAsync(order.Id, _h.Stranger, None)).Error!.Code);
        Assert.Equal("order_not_found", (await _h.PaymentService.StartFoodOrderPaymentAsync(Guid.NewGuid(), _h.Buyer, None)).Error!.Code);

        _h.B.Time.Advance(_h.Food.PaymentWindow + TimeSpan.FromSeconds(1));
        Assert.Equal("payment_window_closed", (await _h.PaymentService.StartFoodOrderPaymentAsync(order.Id, _h.Buyer, None)).Error!.Code);
        Assert.Empty(_h.Gateway.OrderRequests);
    }

    [Fact]
    public async Task With_online_payment_off_an_accepted_order_is_confirmed_and_never_offered_payment()
    {
        await _h.OpenAsync(paymentsOn: false);
        var placed = await _h.PlacedAsync();

        var accepted = await _h.Orders.AcceptAsync(_h.BusinessId, placed.Id, 0, _h.Owner, None);

        Assert.Equal(FoodOrderStatus.Confirmed, accepted.Value!.Status);
        Assert.Equal("payment_not_due", (await _h.PaymentService.StartFoodOrderPaymentAsync(placed.Id, _h.Buyer, None)).Error!.Code);
    }

    // --- The payment arriving ---

    [Fact]
    public async Task A_captured_payment_confirms_the_order_and_books_the_split_to_the_ledger()
    {
        await _h.OpenAsync();
        var (order, _, paymentId) = await _h.PaidAsync();

        Assert.Equal(FoodOrderStatus.Confirmed, order.Status);
        Assert.Equal(FoodOrderActorType.System, order.History[^1].ActorType);
        Assert.Equal(new PaymentView(FoodOrderHarness.Total, "INR", _h.B.Time.GetUtcNow(), paymentId), order.Payment);
        Assert.Null(order.ExpiresAt);

        // 10% commission on the dishes only (285); the restaurant gets the rest of the dishes plus packaging; delivery and GST stay with OnePondy.
        var ledger = await _h.LedgerAsync();
        Assert.Equal(5, ledger.Count);
        Assert.All(ledger, e => Assert.Equal(order.Id, e.FoodOrderId));
        Assert.All(ledger, e => Assert.Null(e.BookingId));
        Assert.Equal(339.25m, await _h.BalanceAsync(LedgerAccounts.GatewayClearing));
        Assert.Equal(-28.50m, await _h.BalanceAsync(LedgerAccounts.PlatformCommission));
        Assert.Equal(-266.50m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(_h.BusinessId)));
        Assert.Equal(-30m, await _h.BalanceAsync(LedgerAccounts.DeliveryFees));
        Assert.Equal(-14.25m, await _h.BalanceAsync(LedgerAccounts.TaxPayable));
        Assert.Equal(0m, ledger.Sum(e => e.Amount));
    }

    [Fact]
    public async Task A_repeated_capture_changes_nothing_and_a_wrong_amount_is_not_applied()
    {
        await _h.OpenAsync();
        var awaiting = await _h.AwaitingPaymentAsync();
        var started = (await _h.PaymentService.StartFoodOrderPaymentAsync(awaiting.Id, _h.Buyer, None)).Value!;

        var wrong = await _h.PaymentService.RecordCaptureAsync(started.OrderId, "pay_1", 300m, None);
        Assert.Equal("payment_amount_mismatch", wrong.Error!.Code);
        Assert.Equal(FoodOrderStatus.AwaitingPayment, (await MineAsync(awaiting.Id))!.Status);

        await _h.PaymentService.RecordCaptureAsync(started.OrderId, "pay_1", FoodOrderHarness.Total, None);
        var again = await _h.PaymentService.RecordCaptureAsync(started.OrderId, "pay_1", FoodOrderHarness.Total, None);

        Assert.Equal(CaptureOutcome.AlreadyRecorded, again.Value!.Outcome);
        Assert.Equal(5, (await _h.LedgerAsync()).Count);
    }

    [Fact]
    public async Task A_second_payment_for_a_paid_order_is_held_and_refunded_not_given_to_the_restaurant()
    {
        await _h.OpenAsync();
        var (order, _, _) = await _h.PaidAsync();
        var duplicate = PaymentAttempt.CreateForFoodOrder(order.Id, "order_dup", FoodOrderHarness.Total, "INR", _h.B.Time.GetUtcNow());
        _h.Db.PaymentAttempts.Add(duplicate);
        await _h.Db.SaveChangesAsync();

        var result = await _h.PaymentService.RecordCaptureAsync("order_dup", "pay_dup", FoodOrderHarness.Total, None);

        Assert.Equal(CaptureOutcome.RefundRaised, result.Value!.Outcome);
        Assert.Equal(FoodOrderStatus.Confirmed, (await MineAsync(order.Id))!.Status);
        Assert.Equal(-266.50m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(_h.BusinessId)));
        Assert.Equal(-339.25m, await _h.BalanceAsync(LedgerAccounts.CustomerRefunds));
        var refund = await _h.Db.Refunds.SingleAsync();
        Assert.Equal((null, order.Id, RefundReason.LatePayment), (refund.BookingId, refund.FoodOrderId, refund.Reason));
        Assert.Equal(33925, Assert.Single(_h.Gateway.RefundRequests).AmountPaise);
    }

    [Fact]
    public async Task Cancelling_while_paying_closes_the_payment_and_a_payment_that_still_lands_is_refunded_in_full()
    {
        await _h.OpenAsync();
        var awaiting = await _h.AwaitingPaymentAsync();
        var started = (await _h.PaymentService.StartFoodOrderPaymentAsync(awaiting.Id, _h.Buyer, None)).Value!;

        var cancelled = await _h.Orders.CancelMineAsync(awaiting.Id, _h.Buyer, "Changed my mind", 0, None);
        Assert.Equal(FoodOrderStatus.Cancelled, cancelled.Value!.Status);
        Assert.Equal(PaymentAttemptStatus.Expired, Assert.Single(await _h.AttemptsAsync(awaiting.Id)).Status);

        // Refunds are off by default, but money nothing wants always goes back.
        var late = await _h.PaymentService.RecordCaptureAsync(started.OrderId, "pay_late", FoodOrderHarness.Total, None);
        Assert.Equal(CaptureOutcome.RefundRaised, late.Value!.Outcome);
        var seen = (await MineAsync(awaiting.Id))!;
        Assert.Equal(FoodOrderStatus.RefundPending, seen.Status);
        Assert.Equal(FoodOrderActorType.System, seen.History[^1].ActorType);
        Assert.Equal(RefundStatus.Pending, Assert.Single(seen.Refunds).Status);

        var refundId = (await _h.Db.Refunds.SingleAsync()).ProviderRefundId!;
        Assert.True((await _h.PaymentService.RecordRefundProcessedAsync(refundId, None)).IsSuccess);

        var done = (await MineAsync(awaiting.Id))!;
        Assert.Equal(FoodOrderStatus.Refunded, done.Status);
        Assert.Equal(RefundStatus.Processed, Assert.Single(done.Refunds).Status);
        Assert.All((await _h.LedgerAsync()).GroupBy(e => e.Account), account => Assert.Equal(0m, account.Sum(e => e.Amount)));
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(_h.BusinessId)));
    }

    [Fact]
    public async Task A_rejected_or_expired_order_closes_its_open_payment()
    {
        await _h.OpenAsync();
        var rejected = await _h.AwaitingPaymentAsync("to-reject");
        await _h.PaymentService.StartFoodOrderPaymentAsync(rejected.Id, _h.Buyer, None);
        var expiring = await _h.AwaitingPaymentAsync("to-expire");
        await _h.PaymentService.StartFoodOrderPaymentAsync(expiring.Id, _h.Buyer, None);

        await _h.Orders.RejectAsync(_h.BusinessId, rejected.Id, 0, _h.Owner, "Out of dosa batter", None);
        _h.B.Time.Advance(_h.Food.PaymentWindow + TimeSpan.FromMinutes(1));
        var result = await _h.Expiry.ExpireDueAsync(None);

        Assert.Equal(new HoldExpiryResult(1, 0, 0), result);
        Assert.Equal(PaymentAttemptStatus.Expired, Assert.Single(await _h.AttemptsAsync(rejected.Id)).Status);
        Assert.Equal(PaymentAttemptStatus.Expired, Assert.Single(await _h.AttemptsAsync(expiring.Id)).Status);
        Assert.Equal("payment_not_due", (await _h.PaymentService.StartFoodOrderPaymentAsync(expiring.Id, _h.Buyer, None)).Error!.Code);
    }

    [Fact]
    public async Task A_payment_landing_after_expiry_is_refunded_and_the_order_stays_expired()
    {
        await _h.OpenAsync();
        var awaiting = await _h.AwaitingPaymentAsync();
        var started = (await _h.PaymentService.StartFoodOrderPaymentAsync(awaiting.Id, _h.Buyer, None)).Value!;
        _h.B.Time.Advance(_h.Food.PaymentWindow + TimeSpan.FromMinutes(1));
        await _h.Expiry.ExpireDueAsync(None);

        var late = await _h.PaymentService.RecordCaptureAsync(started.OrderId, "pay_late", FoodOrderHarness.Total, None);

        Assert.Equal(CaptureOutcome.RefundRaised, late.Value!.Outcome);
        Assert.Equal(FoodOrderStatus.Expired, (await MineAsync(awaiting.Id))!.Status);
        Assert.Single(_h.Gateway.RefundRequests);
    }

    // --- Asking the gateway before giving up on an order ---

    [Fact]
    public async Task Before_expiring_the_worker_asks_the_gateway_and_a_payment_found_there_confirms_the_order()
    {
        await _h.OpenAsync();
        var awaiting = await _h.AwaitingPaymentAsync();
        var started = (await _h.PaymentService.StartFoodOrderPaymentAsync(awaiting.Id, _h.Buyer, None)).Value!;
        _h.Gateway.CustomerPaid(started.OrderId, "pay_lost_webhook", 33925);
        _h.B.Time.Advance(_h.Food.PaymentWindow + TimeSpan.FromMinutes(1));

        var result = await _h.Expiry.ExpireDueAsync(None);

        Assert.Equal(new HoldExpiryResult(0, 1, 0), result);
        Assert.Equal(FoodOrderStatus.Confirmed, (await MineAsync(awaiting.Id))!.Status);
        Assert.Equal(1, _h.Gateway.Lookups);
    }

    [Fact]
    public async Task When_the_gateway_cannot_be_asked_the_order_waits_out_the_grace_period_and_then_expires()
    {
        await _h.OpenAsync();
        var awaiting = await _h.AwaitingPaymentAsync();
        await _h.PaymentService.StartFoodOrderPaymentAsync(awaiting.Id, _h.Buyer, None);
        _h.Gateway.LookupsFail = true;

        _h.B.Time.Advance(_h.Food.PaymentWindow + TimeSpan.FromMinutes(1));
        Assert.Equal(new HoldExpiryResult(0, 0, 1), await _h.Expiry.ExpireDueAsync(None));
        Assert.Equal(FoodOrderStatus.AwaitingPayment, (await MineAsync(awaiting.Id))!.Status);

        _h.B.Time.Advance(_h.Food.GatewayCheckGrace);
        Assert.Equal(new HoldExpiryResult(1, 0, 0), await _h.Expiry.ExpireDueAsync(None));
        Assert.Equal(FoodOrderStatus.Expired, (await MineAsync(awaiting.Id))!.Status);
    }

    [Fact]
    public async Task An_order_never_accepted_expires_without_asking_the_gateway()
    {
        await _h.OpenAsync();
        await _h.PlacedAsync();
        _h.B.Time.Advance(_h.Food.AcceptWindow + TimeSpan.FromMinutes(1));

        Assert.Equal(new HoldExpiryResult(1, 0, 0), await _h.Expiry.ExpireDueAsync(None));
        Assert.Equal(0, _h.Gateway.Lookups);
    }

    [Fact]
    public async Task The_app_asking_is_it_paid_yet_applies_a_payment_found_at_the_gateway_and_strangers_learn_nothing()
    {
        await _h.OpenAsync();
        var awaiting = await _h.AwaitingPaymentAsync();
        var started = (await _h.PaymentService.StartFoodOrderPaymentAsync(awaiting.Id, _h.Buyer, None)).Value!;
        _h.Gateway.CustomerPaid(started.OrderId, "pay_now", 33925);

        Assert.Equal("order_not_found", (await _h.PaymentService.CheckFoodOrderNowAsync(awaiting.Id, _h.Stranger, None))!.Code);
        Assert.Equal(FoodOrderStatus.AwaitingPayment, (await MineAsync(awaiting.Id))!.Status);

        Assert.Null(await _h.PaymentService.CheckFoodOrderNowAsync(awaiting.Id, _h.Buyer, None));
        Assert.Equal(FoodOrderStatus.Confirmed, (await MineAsync(awaiting.Id))!.Status);
    }

    [Fact]
    public async Task Reconciliation_applies_a_food_payment_whose_webhook_never_came_and_names_the_order()
    {
        await _h.OpenAsync();
        var awaiting = await _h.AwaitingPaymentAsync();
        var started = (await _h.PaymentService.StartFoodOrderPaymentAsync(awaiting.Id, _h.Buyer, None)).Value!;
        _h.Gateway.CustomerPaid(started.OrderId, "pay_recon", 33925);

        var summary = await _h.Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(1, summary.AutoResolved);
        Assert.Equal(FoodOrderStatus.Confirmed, (await MineAsync(awaiting.Id))!.Status);
        var recorded = await _h.Db.ReconciliationExceptions.SingleAsync();
        Assert.Equal((null, awaiting.Id), (recorded.BookingId, recorded.FoodOrderId));
    }

    // --- Cancelling a paid order (decision F8) ---

    [Fact]
    public async Task A_paid_order_cannot_be_cancelled_while_refunds_are_switched_off()
    {
        await _h.OpenAsync();
        var (order, _, _) = await _h.PaidAsync();

        var refused = await _h.Orders.CancelMineAsync(order.Id, _h.Buyer, null, 0, None);

        Assert.Equal("refunds_not_enabled", refused.Error!.Code);
        Assert.Contains("order", refused.Error.Detail, StringComparison.Ordinal);
        Assert.Equal(FoodOrderStatus.Confirmed, (await MineAsync(order.Id))!.Status);
        Assert.Empty(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task A_customer_cancelling_a_paid_order_before_cooking_gets_everything_back()
    {
        _h.Payments.RefundsEnabled = true;
        await _h.OpenAsync();
        var (order, _, paymentId) = await _h.PaidAsync();

        var cancelled = await _h.Orders.CancelMineAsync(order.Id, _h.Buyer, "Ordered by mistake", 0, None);

        Assert.True(cancelled.IsSuccess, cancelled.Error?.Detail);
        Assert.Equal(FoodOrderStatus.RefundPending, cancelled.Value!.Status);
        Assert.Equal(
            [FoodOrderStatus.Cancelled, FoodOrderStatus.RefundPending],
            cancelled.Value.History.TakeLast(2).Select(h => h.Status));
        var refund = Assert.Single(cancelled.Value.Refunds);
        Assert.Equal((FoodOrderHarness.Total, RefundReason.CustomerCancelled), (refund.Amount, refund.Reason));
        var sent = Assert.Single(_h.Gateway.RefundRequests);
        Assert.Equal((paymentId, 33925L), (sent.PaymentId, sent.AmountPaise));

        // Nothing is booked until the gateway says the money went back; then every line of the payment is reversed.
        Assert.Equal(5, (await _h.LedgerAsync()).Count);
        var stored = await _h.Db.Refunds.SingleAsync();
        await _h.PaymentService.RecordRefundProcessedAsync(stored.ProviderRefundId!, None);

        Assert.Equal(FoodOrderStatus.Refunded, (await MineAsync(order.Id))!.Status);
        Assert.Equal(10, (await _h.LedgerAsync()).Count);
        Assert.All((await _h.LedgerAsync()).GroupBy(e => e.Account), account => Assert.Equal(0m, account.Sum(e => e.Amount)));
    }

    [Fact]
    public async Task A_restaurant_cancelling_a_paid_order_mid_cooking_refunds_it_in_full_and_once()
    {
        _h.Payments.RefundsEnabled = true;
        await _h.OpenAsync();
        var (order, _, _) = await _h.PaidAsync();
        await _h.Orders.StartPreparingAsync(_h.BusinessId, order.Id, 0, _h.Owner, None);

        var cancelled = await _h.Orders.CancelForBusinessAsync(_h.BusinessId, order.Id, 0, _h.Owner, "Gas ran out", None);
        var again = await _h.Orders.CancelForBusinessAsync(_h.BusinessId, order.Id, 0, _h.Owner, "Gas ran out", None);

        Assert.Equal(FoodOrderStatus.RefundPending, cancelled.Value!.Status);
        Assert.Equal("order_invalid_transition", again.Error!.Code);
        var refund = await _h.Db.Refunds.SingleAsync();
        Assert.Equal((FoodOrderHarness.Total, RefundReason.PartnerCancelled), (refund.Amount, refund.Reason));
        Assert.Single(_h.Gateway.RefundRequests);
    }

    [Fact]
    public async Task A_customer_still_cannot_cancel_once_cooking_starts_even_with_refunds_on()
    {
        _h.Payments.RefundsEnabled = true;
        await _h.OpenAsync();
        var (order, _, _) = await _h.PaidAsync();
        await _h.Orders.StartPreparingAsync(_h.BusinessId, order.Id, 0, _h.Owner, None);

        var refused = await _h.Orders.CancelMineAsync(order.Id, _h.Buyer, null, 0, None);

        Assert.Equal("order_invalid_transition", refused.Error!.Code);
        Assert.Empty(await _h.Db.Refunds.ToListAsync());
    }

    [Fact]
    public async Task A_refund_the_gateway_cannot_take_yet_stays_pending_and_the_sweep_sends_it()
    {
        _h.Payments.RefundsEnabled = true;
        await _h.OpenAsync();
        var (order, _, _) = await _h.PaidAsync();
        _h.Gateway.RefundsFail = true;

        await _h.Orders.CancelMineAsync(order.Id, _h.Buyer, null, 0, None);
        Assert.Null((await _h.Db.Refunds.SingleAsync()).ProviderRefundId);

        _h.Gateway.RefundsFail = false;
        _h.B.Time.Advance(RefundService.SweepAge + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await _h.Refunds.SendUnsentAsync(10, None));
        Assert.NotNull((await _h.Db.Refunds.SingleAsync()).ProviderRefundId);
    }

    // --- Statements for the restaurant ---

    [Fact]
    public async Task A_delivered_paid_order_goes_on_the_restaurants_statement_once_and_agrees_with_the_ledger()
    {
        await _h.OpenAsync();
        var (order, _, _) = await _h.PaidAsync();
        await _h.DeliverAsync(order.Id);
        var today = DateOnly.FromDateTime(_h.B.Time.GetUtcNow().UtcDateTime);

        var draft = await _h.Settlements.DraftAsync(_h.BusinessId, today.AddDays(-6), today, _h.B.Administrator, None);

        Assert.True(draft.IsSuccess, draft.Error?.Detail);
        var statement = draft.Value!;
        Assert.Equal((295m, 28.50m, 0m, 266.50m), (statement.Settlement.GrossAmount, statement.Settlement.CommissionAmount, statement.Settlement.RefundAmount, statement.Settlement.NetAmount));
        var item = Assert.Single(statement.Items);
        Assert.Equal((null, order.Id, "Food order", SettlementItemKind.Sale, 266.50m), (item.BookingId, item.FoodOrderId, item.ListingTitle, item.Kind, item.PartnerAmount));
        Assert.Equal(-await _h.BalanceAsync(LedgerAccounts.PartnerPayable(_h.BusinessId)), statement.Settlement.NetAmount);
        Assert.Contains(order.Id.ToString() + ",Food order,", StatementCsv.Render(statement), StringComparison.Ordinal);

        Assert.Equal("settlement_nothing_due", (await _h.Settlements.DraftAsync(_h.BusinessId, today.AddDays(-1), today, _h.B.Administrator, None)).Error!.Code);

        var approved = (await _h.Settlements.ApproveAsync(statement.Settlement.Id, 0, _h.B.Administrator, None)).Value!;
        Assert.True((await _h.Settlements.MarkPaidAsync(approved.Settlement.Id, 0, "UTR-FOOD-1", _h.B.Administrator, None)).IsSuccess);
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(_h.BusinessId)));
        Assert.Equal(-266.50m, await _h.BalanceAsync(LedgerAccounts.PartnerPaid(_h.BusinessId)));
    }

    [Fact]
    public async Task Orders_not_yet_delivered_or_never_paid_online_are_not_on_a_statement()
    {
        await _h.OpenAsync(paymentsOn: false);
        var unpaid = await _h.PlacedAsync("unpaid");
        await _h.Orders.AcceptAsync(_h.BusinessId, unpaid.Id, 0, _h.Owner, None);
        await _h.DeliverAsync(unpaid.Id);
        var today = DateOnly.FromDateTime(_h.B.Time.GetUtcNow().UtcDateTime);

        Assert.Equal("settlement_nothing_due", (await _h.Settlements.DraftAsync(_h.BusinessId, today, today, _h.B.Administrator, None)).Error!.Code);
    }

    [Fact]
    public async Task A_duplicate_payment_on_a_delivered_order_does_not_block_the_statement()
    {
        await _h.OpenAsync();
        var (order, _, _) = await _h.PaidAsync();
        var duplicate = PaymentAttempt.CreateForFoodOrder(order.Id, "order_dup", FoodOrderHarness.Total, "INR", _h.B.Time.GetUtcNow());
        _h.Db.PaymentAttempts.Add(duplicate);
        await _h.Db.SaveChangesAsync();
        await _h.PaymentService.RecordCaptureAsync("order_dup", "pay_dup", FoodOrderHarness.Total, None);
        await _h.DeliverAsync(order.Id);
        var today = DateOnly.FromDateTime(_h.B.Time.GetUtcNow().UtcDateTime);

        var draft = await _h.Settlements.DraftAsync(_h.BusinessId, today, today, _h.B.Administrator, None);

        Assert.True(draft.IsSuccess, draft.Error?.Detail);
        Assert.Equal(266.50m, draft.Value!.Settlement.NetAmount);
    }

    // --- What administrators see ---

    [Fact]
    public async Task Administrators_see_food_payments_and_refunds_with_the_restaurant_and_can_filter_by_it()
    {
        await _h.OpenAsync();
        var awaiting = await _h.AwaitingPaymentAsync();
        var started = (await _h.PaymentService.StartFoodOrderPaymentAsync(awaiting.Id, _h.Buyer, None)).Value!;
        await _h.Orders.CancelMineAsync(awaiting.Id, _h.Buyer, null, 0, None);
        await _h.PaymentService.RecordCaptureAsync(started.OrderId, "pay_late", FoodOrderHarness.Total, None);

        var payments = await _h.PaymentAdmin.ListPaymentsAsync(new PaymentQuery(null, _h.BusinessId, null, null), None);
        var refunds = await _h.PaymentAdmin.ListRefundsAsync(new RefundQuery(null, null, null), None);
        var elsewhere = await _h.PaymentAdmin.ListPaymentsAsync(new PaymentQuery(null, Guid.NewGuid(), null, null), None);

        var payment = Assert.Single(payments.Items);
        Assert.Equal((null, awaiting.Id, "Food order", _h.BusinessId, "Spice Route"), (payment.BookingId, payment.FoodOrderId, payment.ListingTitle, payment.BusinessId, payment.BusinessName));
        var refund = Assert.Single(refunds.Items);
        Assert.Equal((null, awaiting.Id, "Food order", "Spice Route"), (refund.BookingId, refund.FoodOrderId, refund.ListingTitle, refund.BusinessName));
        Assert.Empty(elsewhere.Items);
    }

    // --- Account deletion ---

    [Fact]
    public async Task An_account_with_a_food_order_in_progress_cannot_be_deleted_by_either_side()
    {
        await _h.OpenAsync();
        await _h.AwaitingPaymentAsync();
        var guard = new BookingsDeletionGuard(_h.Db);

        Assert.Equal([BookingsDeletionGuard.CustomerFoodOrdersInProgress], await guard.GetBlockersAsync(_h.Buyer, None));
        Assert.Equal([BookingsDeletionGuard.PartnerFoodOrdersInProgress], await guard.GetBlockersAsync(_h.Owner, None));
        Assert.Empty(await guard.GetBlockersAsync(_h.Stranger, None));
    }

    // --- The ledger's rules for food ---

    [Fact]
    public void A_ledger_line_is_about_a_booking_or_a_food_order_never_both()
    {
        Assert.Throws<ArgumentException>(() =>
            LedgerEntry.Create(Guid.NewGuid(), LedgerAccounts.GatewayClearing, 1m, Guid.NewGuid(), "x", DateTimeOffset.UnixEpoch, Guid.NewGuid()));
    }

    [Fact]
    public async Task With_no_commission_the_food_split_still_balances_without_a_commission_line()
    {
        _h.Payments.CommissionPercent = 0m;
        await _h.OpenAsync();
        await _h.PaidAsync();

        var ledger = await _h.LedgerAsync();
        Assert.Equal(4, ledger.Count);
        Assert.DoesNotContain(ledger, e => e.Account == LedgerAccounts.PlatformCommission);
        Assert.Equal(-295m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(_h.BusinessId)));
        Assert.Equal(0m, ledger.Sum(e => e.Amount));
    }

    [Fact]
    public async Task A_refund_copies_what_its_payment_paid_for()
    {
        await _h.OpenAsync();
        var (order, _, _) = await _h.PaidAsync();
        var attempt = (await _h.AttemptsAsync(order.Id)).Single();

        var refund = Refund.Of(attempt, attempt.Amount, RefundReason.CustomerCancelled, _h.B.Time.GetUtcNow());

        Assert.Equal((attempt.Id, (Guid?)null, (Guid?)order.Id), (refund.PaymentAttemptId, refund.BookingId, refund.FoodOrderId));
    }
}
