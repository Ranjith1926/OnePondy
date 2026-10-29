using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Bookings;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>
/// Phase 7 step 7.6: the safety net under webhooks. It finds what a lost or late webhook left behind, fixes only what the
/// gateway itself says is complete (recording that it did), and lists everything else for a person.
/// </summary>
public class ReconciliationTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    private ReconciliationService Reconciliation => _h.Bookings.Reconciliation;

    private async Task<List<ReconciliationException>> ExceptionsAsync() =>
        await _h.Bookings.Db.ReconciliationExceptions.AsNoTracking().OrderBy(e => e.FirstSeenAt).ToListAsync();

    private async Task<(BookingDetail Booking, string OrderId)> OpenOrderAsync()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        return (booking, started.OrderId);
    }

    private DateOnly Today => DateOnly.FromDateTime(_h.Bookings.Time.GetUtcNow().UtcDateTime);

    // --- Recent orders: a payment whose webhook was lost ---

    [Fact]
    public async Task A_payment_whose_webhook_was_lost_is_found_applied_and_recorded()
    {
        var (booking, orderId) = await OpenOrderAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_lost", 100_000);

        var summary = await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(1, summary.AutoResolved);
        Assert.Equal(0, summary.Raised);
        Assert.Equal(BookingStatus.Confirmed, (await _h.BookingAsync(booking.Id)).Status);
        var found = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.PaymentAppliedWithoutWebhook, found.Kind);
        Assert.Equal(ReconciliationStatus.AutoResolved, found.Status);
        Assert.Equal("pay_lost", found.ProviderReference);
        Assert.Equal(booking.Id, found.BookingId);
        Assert.NotNull(found.ResolvedAt);
        Assert.Equal(3, (await _h.LedgerAsync()).Count);
    }

    [Fact]
    public async Task Running_the_same_check_again_changes_and_records_nothing_more()
    {
        var (_, orderId) = await OpenOrderAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_lost", 100_000);
        await Reconciliation.RunFrequentChecksAsync(None);

        var again = await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(0, again.Raised + again.AutoResolved);
        Assert.Single(await ExceptionsAsync());
        Assert.Equal(3, (await _h.LedgerAsync()).Count);
    }

    [Fact]
    public async Task A_late_payment_whose_webhook_was_lost_is_found_and_refunded()
    {
        var (booking, orderId) = await OpenOrderAsync();
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        _h.Gateway.CustomerPaid(orderId, "pay_late_lost", 100_000);

        var summary = await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(1, summary.AutoResolved);
        Assert.Equal(BookingStatus.RefundPending, (await _h.BookingAsync(booking.Id)).Status);
        var sent = Assert.Single(_h.Gateway.RefundRequests);
        Assert.Equal("pay_late_lost", sent.PaymentId);
        Assert.Equal(ReconciliationKind.PaymentAppliedWithoutWebhook, Assert.Single(await ExceptionsAsync()).Kind);
    }

    [Fact]
    public async Task An_order_nobody_paid_raises_nothing()
    {
        await OpenOrderAsync();

        var summary = await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(new ReconciliationSummary(1, 0, 0, true), summary);
        Assert.Empty(await ExceptionsAsync());
    }

    [Fact]
    public async Task Orders_older_than_the_recent_window_are_left_to_the_daily_comparison()
    {
        await OpenOrderAsync();
        _h.Bookings.Time.Advance(ReconciliationService.RecentOrderWindow + TimeSpan.FromMinutes(1));
        var before = _h.Gateway.Lookups;

        await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(before, _h.Gateway.Lookups);
    }

    [Fact]
    public async Task When_the_gateway_cannot_be_reached_nothing_is_raised_and_it_says_so()
    {
        var (booking, orderId) = await OpenOrderAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_lost", 100_000);
        _h.Gateway.LookupsFail = true;

        var summary = await Reconciliation.RunFrequentChecksAsync(None);

        Assert.False(summary.GatewayReachable);
        Assert.Empty(await ExceptionsAsync());
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task A_payment_for_the_wrong_amount_is_not_applied_and_is_listed_for_a_person()
    {
        var (booking, orderId) = await OpenOrderAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_short", 99_999);

        var summary = await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(1, summary.Raised);
        Assert.Equal(BookingStatus.AwaitingPayment, (await _h.BookingAsync(booking.Id)).Status);
        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.PaymentMismatch, problem.Kind);
        Assert.Equal(ReconciliationStatus.Open, problem.Status);
        Assert.Equal(1000m, problem.ExpectedAmount);
        Assert.Equal(999.99m, problem.ActualAmount);
        Assert.Empty(await _h.LedgerAsync());
    }

    // --- Refunds ---

    private async Task<(BookingDetail Booking, Refund Refund)> RefundInFlightAsync(bool gatewayAccepted = true)
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        if (!gatewayAccepted)
        {
            _h.Gateway.RefundsFail = true;
        }

        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        return (booking, await _h.Bookings.Db.Refunds.AsNoTracking().SingleAsync());
    }

    [Fact]
    public async Task A_refund_the_gateway_accepted_a_moment_ago_is_not_asked_about_yet()
    {
        await RefundInFlightAsync();

        await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(0, _h.Gateway.RefundLookups);
    }

    [Fact]
    public async Task A_refund_paid_out_at_the_gateway_with_no_webhook_is_applied_and_recorded()
    {
        var (booking, refund) = await RefundInFlightAsync();
        _h.Gateway.SetRefundStatus(refund.ProviderRefundId!, "processed", 100_000);
        _h.Bookings.Time.Advance(ReconciliationService.RefundCheckAge + TimeSpan.FromMinutes(1));

        var summary = await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(1, summary.AutoResolved);
        Assert.Equal(BookingStatus.Refunded, (await _h.BookingAsync(booking.Id)).Status);
        var recorded = (await ExceptionsAsync()).Single(e => e.Kind == ReconciliationKind.RefundAppliedWithoutWebhook);
        Assert.Equal(ReconciliationStatus.AutoResolved, recorded.Status);
        Assert.Equal(6, (await _h.LedgerAsync()).Count);
    }

    [Fact]
    public async Task A_refund_the_gateway_says_failed_is_marked_failed_and_listed_for_a_person()
    {
        var (booking, refund) = await RefundInFlightAsync();
        _h.Gateway.SetRefundStatus(refund.ProviderRefundId!, "failed");
        _h.Bookings.Time.Advance(ReconciliationService.RefundCheckAge + TimeSpan.FromMinutes(1));

        var summary = await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(1, summary.Raised);
        Assert.Equal(RefundStatus.Failed, (await _h.Bookings.Db.Refunds.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(BookingStatus.RefundPending, (await _h.BookingAsync(booking.Id)).Status);
        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.RefundFailed, problem.Kind);
        Assert.Equal(ReconciliationStatus.Open, problem.Status);
    }

    [Fact]
    public async Task A_refund_still_pending_at_the_gateway_is_only_raised_once_it_has_waited_too_long()
    {
        var (_, refund) = await RefundInFlightAsync();
        _h.Gateway.SetRefundStatus(refund.ProviderRefundId!, "pending");

        _h.Bookings.Time.Advance(TimeSpan.FromHours(1));
        await Reconciliation.RunFrequentChecksAsync(None);
        Assert.Empty(await ExceptionsAsync());

        _h.Bookings.Time.Advance(ReconciliationService.RefundStuckAge);
        await Reconciliation.RunFrequentChecksAsync(None);

        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.RefundStuck, problem.Kind);
    }

    [Fact]
    public async Task A_refund_that_never_reached_the_gateway_is_raised_after_the_sweep_has_had_its_chance()
    {
        await RefundInFlightAsync(gatewayAccepted: false);

        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(5));
        await Reconciliation.RunFrequentChecksAsync(None);
        Assert.Empty(await ExceptionsAsync());

        _h.Bookings.Time.Advance(ReconciliationService.RefundNotSentAge);
        await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(ReconciliationKind.RefundNotSent, Assert.Single(await ExceptionsAsync()).Kind);
    }

    [Fact]
    public async Task A_refund_the_webhook_reported_failed_is_listed_once()
    {
        var (_, refund) = await RefundInFlightAsync();
        await _h.Payments.RecordRefundFailedAsync(refund.ProviderRefundId!, None);

        await Reconciliation.RunFrequentChecksAsync(None);
        await Reconciliation.RunFrequentChecksAsync(None);

        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.RefundFailed, problem.Kind);
    }

    // --- Webhooks that did not finish ---

    [Fact]
    public async Task A_webhook_that_could_not_be_applied_is_listed_with_the_reason()
    {
        var failed = GatewayEvent.Receive("evt_bad", "payment.captured", "{}", true, _h.Bookings.Time.GetUtcNow());
        failed.MarkFailed("payment_unknown_order", _h.Bookings.Time.GetUtcNow());
        _h.Bookings.Db.GatewayEvents.Add(failed);
        await _h.Bookings.Db.SaveChangesAsync();

        await Reconciliation.RunFrequentChecksAsync(None);

        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.WebhookNeedsAttention, problem.Kind);
        Assert.Equal(failed.Id, problem.GatewayEventId);
        Assert.Contains("payment_unknown_order", problem.Detail);
    }

    [Fact]
    public async Task A_stored_webhook_is_only_a_problem_once_it_has_gone_unfinished_for_a_while()
    {
        _h.Bookings.Db.GatewayEvents.Add(GatewayEvent.Receive("evt_slow", "payment.captured", "{}", true, _h.Bookings.Time.GetUtcNow()));
        await _h.Bookings.Db.SaveChangesAsync();

        await Reconciliation.RunFrequentChecksAsync(None);
        Assert.Empty(await ExceptionsAsync());

        _h.Bookings.Time.Advance(ReconciliationService.EventUnprocessedAge + TimeSpan.FromMinutes(1));
        await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Equal(ReconciliationKind.WebhookUnprocessed, Assert.Single(await ExceptionsAsync()).Kind);
    }

    // --- The ledger ---

    [Fact]
    public async Task A_payment_with_no_ledger_entries_is_listed()
    {
        var (booking, _, _) = await _h.PaidAsync();
        _h.Bookings.Db.LedgerEntries.RemoveRange(await _h.Bookings.Db.LedgerEntries.ToListAsync());
        await _h.Bookings.Db.SaveChangesAsync();

        await Reconciliation.RunFrequentChecksAsync(None);

        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.LedgerProblem, problem.Kind);
        Assert.Equal(booking.Id, problem.BookingId);
        Assert.Contains("no ledger entries", problem.Detail);
    }

    [Fact]
    public async Task A_payment_whose_entries_are_present_raises_nothing()
    {
        await _h.PaidAsync();

        await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Empty(await ExceptionsAsync());
    }

    [Fact]
    public async Task A_refund_marked_paid_with_no_ledger_entries_is_listed()
    {
        var (_, refund) = await RefundInFlightAsync();
        await _h.Payments.RecordRefundProcessedAsync(refund.ProviderRefundId!, None);
        _h.Bookings.Db.LedgerEntries.RemoveRange((await _h.Bookings.Db.LedgerEntries.ToListAsync()).Where(e => e.Description.Contains("Refund") || e.Description.Contains("returned")));
        await _h.Bookings.Db.SaveChangesAsync();

        await Reconciliation.RunFrequentChecksAsync(None);

        Assert.Contains(await ExceptionsAsync(), e => e.Kind == ReconciliationKind.LedgerProblem && e.RefundId == refund.Id);
    }

    // --- Recording once ---

    [Fact]
    public async Task A_problem_that_is_still_there_is_counted_not_duplicated()
    {
        var (_, orderId) = await OpenOrderAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_short", 99_999);
        await Reconciliation.RunFrequentChecksAsync(None);
        var first = Assert.Single(await ExceptionsAsync());

        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(5));
        await Reconciliation.RunFrequentChecksAsync(None);

        var after = Assert.Single(await ExceptionsAsync());
        Assert.Equal(first.Id, after.Id);
        Assert.Equal(2, after.Occurrences);
        Assert.True(after.LastSeenAt > first.LastSeenAt);
        Assert.Equal(first.FirstSeenAt, after.FirstSeenAt);
    }

    [Fact]
    public async Task A_problem_a_person_resolved_is_not_reopened()
    {
        var (_, orderId) = await OpenOrderAsync();
        _h.Gateway.CustomerPaid(orderId, "pay_short", 99_999);
        await Reconciliation.RunFrequentChecksAsync(None);
        var problem = Assert.Single(await ExceptionsAsync());
        await _h.Bookings.ReconciliationExceptions.ResolveAsync(problem.Id, "Refunded the difference by hand", Guid.NewGuid(), None);

        await Reconciliation.RunFrequentChecksAsync(None);

        var after = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationStatus.Resolved, after.Status);
        Assert.Equal(1, after.Occurrences);
    }

    // --- The daily comparison ---

    [Fact]
    public async Task A_day_where_both_sides_agree_raises_nothing()
    {
        var (_, orderId, paymentId) = await _h.PaidAsync();
        _h.Gateway.ListPayment(orderId, paymentId, 100_000);

        var summary = await Reconciliation.RunDailyAsync(Today, None);

        Assert.True(summary.GatewayReachable);
        Assert.Equal(0, summary.Raised + summary.AutoResolved);
        Assert.Empty(await ExceptionsAsync());
    }

    [Fact]
    public async Task Money_at_the_gateway_for_an_order_we_never_made_is_listed()
    {
        _h.Gateway.ListPayment("order_not_ours", "pay_stranger", 250_000);

        await Reconciliation.RunDailyAsync(Today, None);

        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.GatewayPaymentUnknown, problem.Kind);
        Assert.Equal("pay_stranger", problem.ProviderReference);
        Assert.Equal(2500m, problem.ActualAmount);
    }

    [Fact]
    public async Task A_payment_we_recorded_that_the_gateway_does_not_show_is_listed()
    {
        var (booking, _, paymentId) = await _h.PaidAsync();

        await Reconciliation.RunDailyAsync(Today, None);

        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.PaymentMissingAtGateway, problem.Kind);
        Assert.Equal(booking.Id, problem.BookingId);
        Assert.Equal(paymentId, problem.ProviderReference);
    }

    [Fact]
    public async Task When_the_gateways_list_was_cut_short_not_listed_is_not_taken_to_mean_missing()
    {
        await _h.PaidAsync();
        _h.Gateway.ListIncomplete = true;

        await Reconciliation.RunDailyAsync(Today, None);

        Assert.Empty(await ExceptionsAsync());
    }

    [Fact]
    public async Task A_payment_the_gateway_has_since_refunded_still_counts_as_received()
    {
        var (_, orderId, paymentId) = await _h.PaidAsync();
        _h.Gateway.ListPayment(orderId, paymentId, 100_000, status: "refunded", refundedPaise: 0);

        await Reconciliation.RunDailyAsync(Today, None);

        Assert.DoesNotContain(await ExceptionsAsync(), e => e.Kind == ReconciliationKind.PaymentMissingAtGateway);
    }

    [Fact]
    public async Task A_different_amount_at_the_gateway_is_listed()
    {
        var (_, orderId, paymentId) = await _h.PaidAsync();
        _h.Gateway.ListPayment(orderId, paymentId, 90_000);

        await Reconciliation.RunDailyAsync(Today, None);

        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.PaymentMismatch, problem.Kind);
        Assert.Equal(1000m, problem.ExpectedAmount);
        Assert.Equal(900m, problem.ActualAmount);
    }

    [Fact]
    public async Task A_different_payment_id_for_the_same_order_is_listed()
    {
        var (_, orderId, _) = await _h.PaidAsync();
        _h.Gateway.ListPayment(orderId, "pay_someone_else", 100_000);

        await Reconciliation.RunDailyAsync(Today, None);

        Assert.Contains(await ExceptionsAsync(), e => e.Kind == ReconciliationKind.PaymentMismatch && e.ProviderReference == "pay_someone_else");
    }

    [Fact]
    public async Task A_captured_payment_we_never_recorded_is_applied_and_recorded_by_the_daily_check_too()
    {
        var (booking, orderId) = await OpenOrderAsync();
        _h.Gateway.ListPayment(orderId, "pay_old_lost", 100_000);

        var summary = await Reconciliation.RunDailyAsync(Today, None);

        Assert.Equal(1, summary.AutoResolved);
        Assert.Equal(BookingStatus.Confirmed, (await _h.BookingAsync(booking.Id)).Status);
    }

    [Fact]
    public async Task Refunds_that_do_not_add_up_to_what_the_gateway_has_refunded_are_listed()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, orderId, paymentId) = await _h.PaidAsync();
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None); // refunds 1000

        _h.Gateway.ListPayment(orderId, paymentId, 100_000, refundedPaise: 40_000);
        await Reconciliation.RunDailyAsync(Today, None);

        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.RefundTotalMismatch, problem.Kind);
        Assert.Equal(1000m, problem.ExpectedAmount);
        Assert.Equal(400m, problem.ActualAmount);
    }

    [Fact]
    public async Task Refunds_that_match_the_gateways_total_raise_nothing()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, orderId, paymentId) = await _h.PaidAsync();
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);

        _h.Gateway.ListPayment(orderId, paymentId, 100_000, refundedPaise: 100_000);
        await Reconciliation.RunDailyAsync(Today, None);

        Assert.Empty(await ExceptionsAsync());
    }

    [Fact]
    public async Task When_the_gateway_list_cannot_be_fetched_the_day_is_not_checked_and_nothing_is_raised()
    {
        await _h.PaidAsync();
        _h.Gateway.ListFails = true;

        var summary = await Reconciliation.RunDailyAsync(Today, None);

        Assert.False(summary.GatewayReachable);
        Assert.Empty(await ExceptionsAsync());
    }

    [Fact]
    public async Task A_ledger_that_does_not_add_up_to_zero_is_listed()
    {
        _h.Bookings.Db.LedgerEntries.Add(LedgerEntry.Create(Guid.NewGuid(), LedgerAccounts.GatewayClearing, 10m, null, "Stray", _h.Bookings.Time.GetUtcNow()));
        await _h.Bookings.Db.SaveChangesAsync();

        await Reconciliation.RunDailyAsync(Today, None);

        var problem = Assert.Single(await ExceptionsAsync());
        Assert.Equal(ReconciliationKind.LedgerProblem, problem.Kind);
        Assert.Equal(10m, problem.ActualAmount);
    }

    // --- Which days get reconciled, and when ---

    private async Task<string?> ReconciledThroughAsync() =>
        (await _h.Bookings.Db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == AppSetting.PaymentsReconciledThrough))?.Value;

    [Fact]
    public async Task The_first_ever_run_reconciles_only_yesterday()
    {
        var days = await Reconciliation.RunDueDailyChecksAsync(None);

        Assert.Equal(1, days);
        Assert.Equal("2026-10-27", await ReconciledThroughAsync());
        Assert.Equal(1, _h.Gateway.Listings);
    }

    [Fact]
    public async Task A_day_is_not_reconciled_until_its_late_webhooks_have_had_time_to_arrive()
    {
        await Reconciliation.RunDueDailyChecksAsync(None); // the 27th, done at 04:00 on the 28th
        _h.Bookings.Time.Advance(TimeSpan.FromHours(22)); // 02:00 on the 29th: the 28th ended two hours ago

        Assert.Equal(0, await Reconciliation.RunDueDailyChecksAsync(None));

        _h.Bookings.Time.Advance(TimeSpan.FromHours(2)); // 04:00: past the three-hour wait
        Assert.Equal(1, await Reconciliation.RunDueDailyChecksAsync(None));
        Assert.Equal("2026-10-28", await ReconciledThroughAsync());
    }

    [Fact]
    public async Task Running_again_on_the_same_day_does_nothing_more()
    {
        await Reconciliation.RunDueDailyChecksAsync(None);

        Assert.Equal(0, await Reconciliation.RunDueDailyChecksAsync(None));
        Assert.Equal(1, _h.Gateway.Listings);
    }

    [Fact]
    public async Task Days_missed_while_the_worker_was_off_are_caught_up_in_order()
    {
        await Reconciliation.RunDueDailyChecksAsync(None);
        _h.Bookings.Time.Advance(TimeSpan.FromDays(3)); // the 31st, 04:00: the 28th, 29th and 30th are complete

        var days = await Reconciliation.RunDueDailyChecksAsync(None);

        Assert.Equal(3, days);
        Assert.Equal("2026-10-30", await ReconciledThroughAsync());
    }

    [Fact]
    public async Task A_long_gap_is_caught_up_a_week_at_a_time()
    {
        _h.Bookings.Db.AppSettings.Add(new AppSetting(AppSetting.PaymentsReconciledThrough, "2026-10-01"));
        await _h.Bookings.Db.SaveChangesAsync();

        var days = await Reconciliation.RunDueDailyChecksAsync(None);

        Assert.Equal(ReconciliationService.MaxCatchUpDays, days);
        Assert.Equal("2026-10-08", await ReconciledThroughAsync());
    }

    [Fact]
    public async Task A_day_the_gateway_could_not_be_asked_about_is_retried_not_skipped()
    {
        _h.Gateway.ListFails = true;

        Assert.Equal(0, await Reconciliation.RunDueDailyChecksAsync(None));
        Assert.Null(await ReconciledThroughAsync());

        _h.Gateway.ListFails = false;
        Assert.Equal(1, await Reconciliation.RunDueDailyChecksAsync(None));
        Assert.Equal("2026-10-27", await ReconciledThroughAsync());
    }
}

/// <summary>What an administrator sees and can do with the problems reconciliation lists.</summary>
public class ReconciliationExceptionServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    private ReconciliationExceptionService Service => _h.Bookings.ReconciliationExceptions;

    private async Task<ReconciliationException> SeedAsync(ReconciliationKind kind, string fingerprint, bool autoResolved = false)
    {
        var now = _h.Bookings.Time.GetUtcNow();
        var exception = ReconciliationException.Detect(kind, fingerprint, "Something differs", now, providerReference: "pay_x", actualAmount: 10m);
        if (autoResolved)
        {
            exception.AutoResolve("Applied from the gateway", now);
        }

        _h.Bookings.Db.ReconciliationExceptions.Add(exception);
        await _h.Bookings.Db.SaveChangesAsync();
        return exception;
    }

    [Fact]
    public async Task The_list_is_newest_first_and_can_be_filtered_by_status_and_kind()
    {
        await SeedAsync(ReconciliationKind.RefundStuck, "a");
        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(1));
        await SeedAsync(ReconciliationKind.PaymentMismatch, "b");
        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(1));
        await SeedAsync(ReconciliationKind.WebhookUnprocessed, "c", autoResolved: true);

        var all = await Service.ListAsync(new ReconciliationQuery(null, null, null, null), None);
        var open = await Service.ListAsync(new ReconciliationQuery(ReconciliationStatus.Open, null, null, null), None);
        var mismatches = await Service.ListAsync(new ReconciliationQuery(null, ReconciliationKind.PaymentMismatch, null, null), None);

        Assert.Equal(3, all.TotalCount);
        Assert.Equal([ReconciliationKind.WebhookUnprocessed, ReconciliationKind.PaymentMismatch, ReconciliationKind.RefundStuck], all.Items.Select(e => e.Kind));
        Assert.Equal(2, open.TotalCount);
        Assert.Equal(ReconciliationKind.PaymentMismatch, Assert.Single(mismatches.Items).Kind);
        Assert.Equal(2, await Service.OpenCountAsync(None));
    }

    [Fact]
    public async Task The_list_is_paged()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(ReconciliationKind.RefundStuck, $"f{i}");
            _h.Bookings.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var second = await Service.ListAsync(new ReconciliationQuery(null, null, 2, 2), None);

        Assert.Equal(5, second.TotalCount);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(2, second.Page);
    }

    [Fact]
    public async Task An_administrator_resolves_an_open_problem_with_a_note_and_it_is_audited()
    {
        var problem = await SeedAsync(ReconciliationKind.RefundFailed, "a");
        var staff = Guid.NewGuid();

        var resolved = await Service.ResolveAsync(problem.Id, "  Paid the customer by bank transfer.  ", staff, None);

        Assert.Equal(ReconciliationStatus.Resolved, resolved.Value!.Status);
        Assert.Equal("Paid the customer by bank transfer.", resolved.Value.ResolutionNote);
        Assert.NotNull(resolved.Value.ResolvedAt);
        var entry = Assert.Single(await _h.Bookings.Db.AuditEntries.AsNoTracking().Where(e => e.Action == "reconciliation_exception.resolved").ToListAsync());
        Assert.Equal("reconciliation_exception.resolved", entry.Action);
        Assert.Equal(staff, entry.ActorUserId);
        Assert.Equal(problem.Id.ToString(), entry.EntityId);
        Assert.Equal(0, await Service.OpenCountAsync(None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolving_needs_a_note_saying_what_was_done(string? note)
    {
        var problem = await SeedAsync(ReconciliationKind.RefundFailed, "a");

        var result = await Service.ResolveAsync(problem.Id, note, Guid.NewGuid(), None);

        Assert.Equal("validation_failed", result.Error!.Code);
        Assert.Equal(ReconciliationStatus.Open, (await _h.Bookings.Db.ReconciliationExceptions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task A_problem_cannot_be_resolved_twice_and_an_unknown_one_is_not_found()
    {
        var problem = await SeedAsync(ReconciliationKind.RefundFailed, "a");
        await Service.ResolveAsync(problem.Id, "Done", Guid.NewGuid(), None);

        var again = await Service.ResolveAsync(problem.Id, "Done again", Guid.NewGuid(), None);
        var unknown = await Service.ResolveAsync(Guid.NewGuid(), "Done", Guid.NewGuid(), None);

        Assert.Equal("reconciliation_exception_closed", again.Error!.Code);
        Assert.Equal("reconciliation_exception_not_found", unknown.Error!.Code);
    }

    [Fact]
    public async Task A_difference_the_system_already_fixed_needs_no_person()
    {
        var fixedByTheSystem = await SeedAsync(ReconciliationKind.PaymentAppliedWithoutWebhook, "a", autoResolved: true);

        var result = await Service.ResolveAsync(fixedByTheSystem.Id, "Looked at it", Guid.NewGuid(), None);

        Assert.Equal("reconciliation_exception_closed", result.Error!.Code);
    }
}
