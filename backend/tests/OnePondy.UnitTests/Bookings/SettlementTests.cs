using OnePondy.Application.Bookings;
using OnePondy.Application.Common;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>Phase 7 step 7.9: partner statements. Every amount comes from the ledger, and a statement that disagrees with it is refused.</summary>
public class SettlementTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    private SettlementService Settlements => _h.Bookings.Settlements;

    private Guid BusinessId => _h.Bookings.BusinessId;

    private Guid Admin => _h.Bookings.Administrator;

    private DateOnly Today => DateOnly.FromDateTime(_h.Bookings.Time.GetUtcNow().UtcDateTime);

    /// <summary>A booking that was paid and then marked completed by the partner.</summary>
    private async Task<BookingDetail> CompletedAsync(string key = "idem-done")
    {
        var (booking, _, _) = await _h.PaidAsync(key);
        return (await _h.Bookings.Bookings.CompleteAsync(BusinessId, booking.Id, booking.Version, _h.Bookings.Owner, None)).Value!;
    }

    private Task<Result<SettlementDetail>> DraftAsync(DateOnly? end = null, DateOnly? start = null) =>
        Settlements.DraftAsync(BusinessId, start ?? Today.AddDays(-7), end ?? Today, Admin, None);

    private async Task<decimal> OwedToPartnerAsync() => -await _h.BalanceAsync(LedgerAccounts.PartnerPayable(BusinessId));

    // --- Drafting ---

    [Fact]
    public async Task A_completed_paid_booking_makes_a_statement_that_matches_the_ledger()
    {
        var booking = await CompletedAsync();

        var draft = (await DraftAsync()).Value!;

        var s = draft.Settlement;
        Assert.Equal((1000m, 100m, 0m, 900m, SettlementStatus.Draft), (s.GrossAmount, s.CommissionAmount, s.RefundAmount, s.NetAmount, s.Status));
        Assert.Equal(await OwedToPartnerAsync(), s.NetAmount);
        var line = Assert.Single(draft.Items);
        Assert.Equal((booking.Id, SettlementItemKind.Sale, 900m), (line.BookingId, line.Kind, line.PartnerAmount));
        Assert.Equal("Backwater Kayaking", line.ListingTitle);
        Assert.False(string.IsNullOrEmpty(s.BusinessName));
    }

    [Fact]
    public async Task A_booking_paid_twice_is_settled_on_the_payment_that_confirmed_it()
    {
        // The second payment was held for the customer and refunded; it never reached the partner, so it is no part of the sale.
        var (booking, _, _) = await _h.PaidAsync();
        var duplicate = PaymentAttempt.Create(booking.Id, "order_dup", 1000m, "INR", _h.Bookings.Time.GetUtcNow());
        _h.Bookings.Db.PaymentAttempts.Add(duplicate);
        await _h.Bookings.Db.SaveChangesAsync();
        Assert.Equal(CaptureOutcome.RefundRaised, (await _h.Payments.RecordCaptureAsync("order_dup", "pay_dup", 1000m, None)).Value!.Outcome);
        var current = await _h.BookingAsync(booking.Id);
        await _h.Bookings.Bookings.CompleteAsync(BusinessId, current.Id, current.Version, _h.Bookings.Owner, None);

        var draft = await DraftAsync();

        Assert.True(draft.IsSuccess, draft.Error?.Detail);
        Assert.Equal((1000m, 100m, 900m), (draft.Value!.Settlement.GrossAmount, draft.Value.Settlement.CommissionAmount, draft.Value.Settlement.NetAmount));
        Assert.Equal(await OwedToPartnerAsync(), draft.Value.Settlement.NetAmount);
    }

    [Fact]
    public async Task A_booking_that_is_only_confirmed_is_not_on_a_statement_yet()
    {
        await _h.PaidAsync();

        var result = await DraftAsync();

        Assert.Equal("settlement_nothing_due", result.Error!.Code);
    }

    [Fact]
    public async Task A_completed_booking_that_was_never_paid_online_owes_the_partner_nothing_here()
    {
        var created = (await _h.Bookings.Bookings.CreateAsync(_h.Bookings.Customer, _h.Bookings.Input(idempotencyKey: "offline"), None)).Value!;
        var accepted = (await _h.Bookings.Bookings.AcceptAsync(BusinessId, created.Id, created.Version, _h.Bookings.Owner, None)).Value!;
        await _h.Bookings.Bookings.CompleteAsync(BusinessId, accepted.Id, accepted.Version, _h.Bookings.Owner, None);

        var result = await DraftAsync();

        Assert.Equal("settlement_nothing_due", result.Error!.Code);
    }

    [Fact]
    public async Task A_booking_refunded_in_full_never_appears()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        await _h.Bookings.Bookings.CancelForBusinessAsync(BusinessId, booking.Id, booking.Version, _h.Bookings.Owner, null, None);
        await _h.Payments.RecordRefundProcessedAsync("rfnd_test_1", None);

        var result = await DraftAsync();

        Assert.Equal("settlement_nothing_due", result.Error!.Code);
        Assert.Equal(0m, await OwedToPartnerAsync());
    }

    [Fact]
    public async Task A_late_cancellation_keeps_the_partners_share_of_what_was_not_refunded()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        _h.Bookings.Time.Advance(TimeSpan.FromHours(2)); // past the free deadline: half is refunded
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        await _h.Payments.RecordRefundProcessedAsync("rfnd_test_1", None);

        var draft = (await DraftAsync()).Value!;

        var s = draft.Settlement;
        // Sold 1000 (commission 100), gave back 500 (commission 50 returned): the partner keeps 450.
        Assert.Equal((1000m, 50m, 500m, 450m), (s.GrossAmount, s.CommissionAmount, s.RefundAmount, s.NetAmount));
        Assert.Equal(await OwedToPartnerAsync(), s.NetAmount);
        Assert.Equal([SettlementItemKind.Sale, SettlementItemKind.Refund], draft.Items.Select(i => i.Kind).Order());
        Assert.Equal(450m, draft.Items.Sum(i => i.PartnerAmount));
    }

    [Fact]
    public async Task A_booking_finished_after_the_period_waits_for_a_later_statement()
    {
        await CompletedAsync();
        var yesterday = Today.AddDays(-1);

        var result = await DraftAsync(end: yesterday, start: yesterday.AddDays(-7));

        Assert.Equal("settlement_nothing_due", result.Error!.Code);
        Assert.True((await DraftAsync()).IsSuccess);
    }

    [Fact]
    public async Task A_booking_is_never_on_two_statements()
    {
        await CompletedAsync();
        await DraftAsync(start: Today.AddDays(-7));

        var again = await DraftAsync(start: Today.AddDays(-14));

        Assert.Equal("settlement_nothing_due", again.Error!.Code);
    }

    [Fact]
    public async Task The_same_period_cannot_be_drafted_twice()
    {
        await CompletedAsync();
        await DraftAsync();

        Assert.Equal("settlement_period_exists", (await DraftAsync()).Error!.Code);
    }

    [Fact]
    public async Task A_period_must_be_whole_days_that_have_finished()
    {
        await CompletedAsync();

        var future = await DraftAsync(end: Today.AddDays(1));
        var backwards = await Settlements.DraftAsync(BusinessId, Today, Today.AddDays(-1), Admin, None);
        var unknown = await Settlements.DraftAsync(Guid.NewGuid(), Today.AddDays(-7), Today, Admin, None);

        Assert.Equal(ErrorKind.Validation, future.Error!.Kind);
        Assert.Equal(ErrorKind.Validation, backwards.Error!.Kind);
        Assert.Equal("settlement_business_not_found", unknown.Error!.Code);
    }

    [Fact]
    public async Task Discarding_a_draft_puts_its_bookings_back_for_the_next_statement()
    {
        await CompletedAsync();
        var draft = (await DraftAsync()).Value!;

        Assert.Null(await Settlements.DiscardDraftAsync(draft.Settlement.Id, draft.Settlement.Version, Admin, None));

        Assert.Equal("settlement_not_found", (await Settlements.GetAsync(draft.Settlement.Id, None)).Error!.Code);
        Assert.True((await DraftAsync()).IsSuccess);
    }

    // --- Approving and paying ---

    private async Task<SettlementDetail> ApprovedAsync()
    {
        await CompletedAsync();
        var draft = (await DraftAsync()).Value!;
        return (await Settlements.ApproveAsync(draft.Settlement.Id, draft.Settlement.Version, Admin, None)).Value!;
    }

    [Fact]
    public async Task A_statement_goes_from_draft_to_approved_to_paid_and_the_payout_is_booked()
    {
        var approved = await ApprovedAsync();
        Assert.Equal(SettlementStatus.Approved, approved.Settlement.Status);
        Assert.NotNull(approved.Settlement.ApprovedAt);
        Assert.Equal(900m, await OwedToPartnerAsync());

        var paid = (await Settlements.MarkPaidAsync(approved.Settlement.Id, approved.Settlement.Version, "UTR123456", Admin, None)).Value!;

        Assert.Equal((SettlementStatus.Paid, "UTR123456"), (paid.Settlement.Status, paid.Settlement.PayoutReference));
        Assert.NotNull(paid.Settlement.PaidAt);
        Assert.Equal(0m, await OwedToPartnerAsync());
        Assert.Equal(-900m, await _h.BalanceAsync(LedgerAccounts.PartnerPaid(BusinessId)));
        foreach (var transaction in (await _h.LedgerAsync()).GroupBy(e => e.TransactionId))
        {
            Assert.Equal(0m, transaction.Sum(e => e.Amount));
        }
    }

    [Fact]
    public async Task A_payout_needs_the_banks_reference()
    {
        var approved = await ApprovedAsync();

        var result = await Settlements.MarkPaidAsync(approved.Settlement.Id, approved.Settlement.Version, "  ", Admin, None);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal(900m, await OwedToPartnerAsync());
    }

    [Fact]
    public async Task A_draft_cannot_be_paid_and_a_paid_statement_cannot_be_paid_or_changed_again()
    {
        await CompletedAsync();
        var draft = (await DraftAsync()).Value!.Settlement;
        Assert.Equal("settlement_invalid_state", (await Settlements.MarkPaidAsync(draft.Id, draft.Version, "UTR1", Admin, None)).Error!.Code);

        var approved = (await Settlements.ApproveAsync(draft.Id, draft.Version, Admin, None)).Value!.Settlement;
        var paid = (await Settlements.MarkPaidAsync(approved.Id, approved.Version, "UTR1", Admin, None)).Value!.Settlement;

        Assert.Equal("settlement_invalid_state", (await Settlements.MarkPaidAsync(paid.Id, paid.Version, "UTR2", Admin, None)).Error!.Code);
        Assert.Equal("settlement_invalid_state", (await Settlements.ApproveAsync(paid.Id, paid.Version, Admin, None)).Error!.Code);
        Assert.Equal("settlement_invalid_state", (await Settlements.DiscardDraftAsync(paid.Id, paid.Version, Admin, None))?.Code);
        Assert.Equal(-900m, await _h.BalanceAsync(LedgerAccounts.PartnerPaid(BusinessId)));
    }

    [Fact]
    public async Task An_approved_statement_cannot_be_thrown_away()
    {
        var approved = (await ApprovedAsync()).Settlement;

        Assert.Equal("settlement_invalid_state", (await Settlements.DiscardDraftAsync(approved.Id, approved.Version, Admin, None))?.Code);
    }

    [Fact]
    public async Task Each_step_is_recorded_in_the_audit_trail()
    {
        var approved = (await ApprovedAsync()).Settlement;
        await Settlements.MarkPaidAsync(approved.Id, approved.Version, "UTR9", Admin, None);

        var actions = _h.Bookings.Db.AuditEntries.Where(e => e.EntityType == "settlement").Select(e => e.Action).ToList();

        Assert.Equal(new[] { "settlement.approved", "settlement.drafted", "settlement.paid" }, actions.Order());
    }

    // --- What a partner sees ---

    [Fact]
    public async Task A_partner_sees_approved_and_paid_statements_but_not_drafts()
    {
        await CompletedAsync();
        var draft = (await DraftAsync()).Value!.Settlement;
        var owner = _h.Bookings.Owner;

        var beforeApproval = (await Settlements.ListForBusinessAsync(BusinessId, owner, None)).Value!;
        var hidden = await Settlements.GetForBusinessAsync(BusinessId, draft.Id, owner, None);
        await Settlements.ApproveAsync(draft.Id, draft.Version, Admin, None);
        var afterApproval = (await Settlements.ListForBusinessAsync(BusinessId, owner, None)).Value!;
        var shown = await Settlements.GetForBusinessAsync(BusinessId, draft.Id, owner, None);

        Assert.Empty(beforeApproval);
        Assert.Equal("settlement_not_found", hidden.Error!.Code);
        Assert.Equal(draft.Id, Assert.Single(afterApproval).Id);
        Assert.Equal(900m, shown.Value!.Settlement.NetAmount);
    }

    [Fact]
    public async Task Nobody_else_can_see_a_partners_statements()
    {
        var approved = (await ApprovedAsync()).Settlement;
        var stranger = _h.Bookings.Customer;

        Assert.Equal("settlement_not_found", (await Settlements.ListForBusinessAsync(BusinessId, stranger, None)).Error!.Code);
        Assert.Equal("settlement_not_found", (await Settlements.GetForBusinessAsync(BusinessId, approved.Id, stranger, None)).Error!.Code);
        Assert.Equal("settlement_not_found", (await Settlements.CsvForBusinessAsync(BusinessId, approved.Id, stranger, None)).Error!.Code);
    }

    [Fact]
    public async Task The_csv_has_the_totals_and_one_row_per_booking()
    {
        var approved = (await ApprovedAsync()).Settlement;

        var file = (await Settlements.CsvForBusinessAsync(BusinessId, approved.Id, _h.Bookings.Owner, None)).Value!;

        Assert.Equal($"statement-{approved.PeriodStart:yyyy-MM-dd}-to-{approved.PeriodEnd:yyyy-MM-dd}.csv", file.FileName);
        var lines = file.Content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Gross sales,1000.00", lines);
        Assert.Contains("Commission,100.00", lines);
        Assert.Contains("Net payable,900.00", lines);
        Assert.Contains(lines, l => l.EndsWith(",Sale,1000.00,100.00,900.00", StringComparison.Ordinal) && l.Contains("Backwater Kayaking", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("Kayaks, boats and more", "\"Kayaks, boats and more\"")]
    [InlineData("Plain title", "Plain title")]
    public void Text_typed_by_a_partner_cannot_become_a_spreadsheet_formula(string typed, string expectedCell)
    {
        var detail = new SettlementDetail(
            new SettlementSummary(Guid.NewGuid(), Guid.NewGuid(), "Biz", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), 100m, 10m, 0m, 90m, SettlementStatus.Approved, null, DateTimeOffset.UnixEpoch, null, null, 1),
            [new SettlementItemView(Guid.NewGuid(), null, typed, null, SettlementItemKind.Sale, 100m, 10m, 90m)]);

        var row = StatementCsv.Render(detail).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Last();

        Assert.Contains("," + expectedCell + ",", row, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refund_amount_keeps_its_minus_sign_in_the_csv()
    {
        var detail = new SettlementDetail(
            new SettlementSummary(Guid.NewGuid(), Guid.NewGuid(), "Biz", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), 0m, 0m, 0m, 0m, SettlementStatus.Approved, null, DateTimeOffset.UnixEpoch, null, null, 1),
            [new SettlementItemView(Guid.NewGuid(), null, "Kayaking", null, SettlementItemKind.Refund, 500m, 50m, -450m)]);

        Assert.EndsWith(",Refund,500.00,50.00,-450.00", StatementCsv.Render(detail).TrimEnd(), StringComparison.Ordinal);
    }

    // --- Administrators' views of money in and out ---

    [Fact]
    public async Task Administrators_list_payments_and_refunds_with_the_listing_and_business()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, paymentId) = await _h.PaidAsync();
        _h.Bookings.Time.Advance(TimeSpan.FromHours(2));
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);

        var payments = await _h.Bookings.PaymentAdmin.ListPaymentsAsync(new PaymentQuery(PaymentAttemptStatus.Captured, null, null, null), None);
        var refunds = await _h.Bookings.PaymentAdmin.ListRefundsAsync(new RefundQuery(RefundStatus.Pending, null, null), None);
        var none = await _h.Bookings.PaymentAdmin.ListPaymentsAsync(new PaymentQuery(PaymentAttemptStatus.Failed, null, null, null), None);

        var payment = Assert.Single(payments.Items);
        Assert.Equal((booking.Id, "Backwater Kayaking", 1000m, paymentId), (payment.BookingId, payment.ListingTitle, payment.Amount, payment.ProviderPaymentId));
        Assert.False(string.IsNullOrEmpty(payment.BusinessName));
        var refund = Assert.Single(refunds.Items);
        Assert.Equal((500m, RefundReason.CustomerCancelled), (refund.Amount, refund.Reason));
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task Payments_can_be_filtered_by_business()
    {
        await _h.PaidAsync();

        var mine = await _h.Bookings.PaymentAdmin.ListPaymentsAsync(new PaymentQuery(null, BusinessId, null, null), None);
        var other = await _h.Bookings.PaymentAdmin.ListPaymentsAsync(new PaymentQuery(null, Guid.NewGuid(), null, null), None);

        Assert.Single(mine.Items);
        Assert.Empty(other.Items);
    }
}
