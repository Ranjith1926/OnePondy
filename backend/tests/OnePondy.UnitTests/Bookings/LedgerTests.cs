using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Listings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>Phase 7 step 7.5: the double-entry ledger. Every event adds up to zero, and no event is booked twice.</summary>
public class LedgerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    private async Task<IReadOnlyList<LedgerEntry>> EntriesAsync() => await _h.LedgerAsync();

    private static void AssertEveryTransactionBalances(IEnumerable<LedgerEntry> entries)
    {
        foreach (var transaction in entries.GroupBy(e => e.TransactionId))
        {
            Assert.Equal(0m, transaction.Sum(e => e.Amount));
        }
    }

    // --- A payment confirms a booking ---

    [Fact]
    public async Task A_payment_is_booked_as_money_at_the_gateway_the_partners_share_and_the_commission()
    {
        var (booking, _, _) = await _h.PaidAsync();
        var partnerAccount = LedgerAccounts.PartnerPayable(booking.BusinessId);

        var entries = await EntriesAsync();

        Assert.Equal(3, entries.Count);
        Assert.Equal(1000m, await _h.BalanceAsync(LedgerAccounts.GatewayClearing));
        Assert.Equal(-100m, await _h.BalanceAsync(LedgerAccounts.PlatformCommission));
        Assert.Equal(-900m, await _h.BalanceAsync(partnerAccount));
        Assert.All(entries, e => Assert.Equal(booking.Id, e.BookingId));
        AssertEveryTransactionBalances(entries);
    }

    [Fact]
    public async Task The_commission_rate_is_a_setting_and_is_taken_at_the_time_of_payment()
    {
        _h.Bookings.Payments.CommissionPercent = 12.5m;
        var (booking, _, _) = await _h.PaidAsync();

        Assert.Equal(-125m, await _h.BalanceAsync(LedgerAccounts.PlatformCommission));
        Assert.Equal(-875m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(booking.BusinessId)));
    }

    [Fact]
    public async Task With_no_commission_the_whole_payment_is_the_partners()
    {
        _h.Bookings.Payments.CommissionPercent = 0m;
        var (booking, _, _) = await _h.PaidAsync();

        var entries = await EntriesAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal(-1000m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(booking.BusinessId)));
        AssertEveryTransactionBalances(entries);
    }

    [Fact]
    public async Task The_same_payment_reported_again_books_nothing_more()
    {
        var (_, orderId, paymentId) = await _h.PaidAsync();

        await _h.Payments.RecordCaptureAsync(orderId, paymentId, 1000m, None);
        await _h.Payments.RecordCaptureAsync(orderId, paymentId, 1000m, None);

        Assert.Equal(3, (await EntriesAsync()).Count);
    }

    [Fact]
    public async Task A_payment_that_was_not_applied_books_nothing()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;

        await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_wrong", 999m, None);

        Assert.Empty(await EntriesAsync());
    }

    // --- Refunds ---

    [Fact]
    public async Task A_full_refund_leaves_every_account_at_zero()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        var refund = await _h.Bookings.Db.Refunds.SingleAsync();

        await _h.Payments.RecordRefundProcessedAsync(refund.ProviderRefundId!, None);

        var entries = await EntriesAsync();
        Assert.Equal(6, entries.Count);
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.GatewayClearing));
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.PlatformCommission));
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(booking.BusinessId)));
        AssertEveryTransactionBalances(entries);
    }

    [Fact]
    public async Task A_partial_refund_gives_back_the_same_share_of_the_commission_and_the_partners_part()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        _h.Bookings.Time.Advance(TimeSpan.FromHours(2)); // past the 24-hour free-cancellation deadline: 50% back
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        var refund = await _h.Bookings.Db.Refunds.SingleAsync();
        Assert.Equal(500m, refund.Amount);

        await _h.Payments.RecordRefundProcessedAsync(refund.ProviderRefundId!, None);

        Assert.Equal(500m, await _h.BalanceAsync(LedgerAccounts.GatewayClearing));
        Assert.Equal(-50m, await _h.BalanceAsync(LedgerAccounts.PlatformCommission));
        Assert.Equal(-450m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(booking.BusinessId)));
        Assert.Equal(0m, (await EntriesAsync()).Sum(e => e.Amount));
    }

    [Fact]
    public async Task A_refund_returns_the_commission_that_was_booked_not_todays_rate()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        _h.Bookings.Payments.CommissionPercent = 10m;
        var (booking, _, _) = await _h.PaidAsync();
        _h.Bookings.Payments.CommissionPercent = 25m; // the rate changes after the payment
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        var refund = await _h.Bookings.Db.Refunds.SingleAsync();

        await _h.Payments.RecordRefundProcessedAsync(refund.ProviderRefundId!, None);

        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.PlatformCommission));
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(booking.BusinessId)));
    }

    [Fact]
    public async Task The_same_refund_reported_processed_twice_is_booked_once()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);
        var refund = await _h.Bookings.Db.Refunds.SingleAsync();

        await _h.Payments.RecordRefundProcessedAsync(refund.ProviderRefundId!, None);
        await _h.Payments.RecordRefundProcessedAsync(refund.ProviderRefundId!, None);

        Assert.Equal(6, (await EntriesAsync()).Count);
    }

    [Fact]
    public async Task A_refund_that_has_not_been_confirmed_by_the_gateway_books_nothing_yet()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();

        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);

        // Only the payment's three lines: the money has not gone back until the gateway says so.
        Assert.Equal(3, (await EntriesAsync()).Count);
        Assert.Equal(1000m, await _h.BalanceAsync(LedgerAccounts.GatewayClearing));
    }

    // --- A payment no booking wanted ---

    [Fact]
    public async Task A_late_payment_is_held_for_the_customer_and_nets_to_zero_once_refunded()
    {
        var booking = await _h.AwaitingPaymentAsync();
        var started = (await _h.Payments.StartPaymentAsync(booking.Id, _h.Bookings.Customer, None)).Value!;
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, _h.Bookings.Customer, null, booking.Version, None);

        await _h.Payments.RecordCaptureAsync(started.OrderId, "pay_late", 1000m, None);

        Assert.Equal(1000m, await _h.BalanceAsync(LedgerAccounts.GatewayClearing));
        Assert.Equal(-1000m, await _h.BalanceAsync(LedgerAccounts.CustomerRefunds));
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.PlatformCommission));

        var refund = await _h.Bookings.Db.Refunds.SingleAsync();
        await _h.Payments.RecordRefundProcessedAsync(refund.ProviderRefundId!, None);

        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.GatewayClearing));
        Assert.Equal(0m, await _h.BalanceAsync(LedgerAccounts.CustomerRefunds));
        AssertEveryTransactionBalances(await EntriesAsync());
    }

    [Fact]
    public async Task A_duplicate_payment_for_a_paid_booking_is_held_not_given_to_the_partner()
    {
        var (booking, _, _) = await _h.PaidAsync();
        var second = PaymentAttempt.Create(booking.Id, "order_second", 1000m, "INR", _h.Bookings.Time.GetUtcNow());
        _h.Bookings.Db.PaymentAttempts.Add(second);
        await _h.Bookings.Db.SaveChangesAsync();

        await _h.Payments.RecordCaptureAsync("order_second", "pay_dup", 1000m, None);

        Assert.Equal(-900m, await _h.BalanceAsync(LedgerAccounts.PartnerPayable(booking.BusinessId)));
        Assert.Equal(-1000m, await _h.BalanceAsync(LedgerAccounts.CustomerRefunds));
        Assert.Equal(2000m, await _h.BalanceAsync(LedgerAccounts.GatewayClearing));
    }

    // --- The arithmetic ---

    [Theory]
    [InlineData(1000, 10, 100)]
    [InlineData(999.99, 10, 100.00)]
    [InlineData(0.05, 10, 0.01)]
    [InlineData(0.04, 10, 0.00)]
    [InlineData(1234.57, 12.5, 154.32)]
    [InlineData(500, 0, 0)]
    [InlineData(500, 100, 500)]
    public void Commission_is_rounded_to_the_paisa_with_halves_going_up(double amount, double percent, double expected)
    {
        Assert.Equal((decimal)expected, LedgerService.CommissionFor((decimal)amount, (decimal)percent));
    }

    [Fact]
    public async Task Whatever_the_amount_and_rate_a_payment_balances_and_the_two_shares_add_back_to_the_total()
    {
        foreach (var amount in new[] { 0.01m, 0.99m, 1m, 333.33m, 999.99m, 1234.57m, 99999.99m })
        {
            foreach (var percent in new[] { 0m, 0.5m, 10m, 12.5m, 33.33m, 99.99m, 100m })
            {
                var db = TestDbContextFor();
                var options = Options.Create(new PaymentsOptions { CommissionPercent = percent });
                var ledger = new LedgerService(db, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), options);
                var booking = Booking.CreateHold(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ListingType.Activity, "k", null, null, 1,
                    new PriceSnapshot(amount, 1, amount, 0m, 0m, 0m, amount, "INR"), new CancellationPolicy(0, 0, null), DateTimeOffset.UtcNow);
                var attempt = PaymentAttempt.Create(booking.Id, "order_x", amount, "INR", DateTimeOffset.UtcNow);

                await ledger.PostCaptureAsync(booking, attempt, None);
                await db.SaveChangesAsync();

                var entries = await db.LedgerEntries.ToListAsync();
                Assert.Equal(0m, entries.Sum(e => e.Amount));
                Assert.Equal(amount, entries.Single(e => e.Account == LedgerAccounts.GatewayClearing).Amount);
                var commission = -entries.Where(e => e.Account == LedgerAccounts.PlatformCommission).Sum(e => e.Amount);
                var partner = -entries.Where(e => e.Account.StartsWith("partner_payable:", StringComparison.Ordinal)).Sum(e => e.Amount);
                Assert.Equal(amount, commission + partner);
                Assert.True(commission >= 0m && partner >= 0m, $"{amount} at {percent}%: commission {commission}, partner {partner}");
            }
        }
    }

    [Fact]
    public void A_transaction_id_is_stable_for_an_event_and_different_for_different_events()
    {
        var attempt = Guid.NewGuid();

        Assert.Equal(LedgerService.TransactionId(LedgerService.CaptureKey(attempt)), LedgerService.TransactionId(LedgerService.CaptureKey(attempt)));
        Assert.NotEqual(LedgerService.TransactionId(LedgerService.CaptureKey(attempt)), LedgerService.TransactionId(LedgerService.RefundKey(attempt)));
        Assert.NotEqual(LedgerService.TransactionId(LedgerService.CaptureKey(attempt)), LedgerService.TransactionId(LedgerService.CaptureKey(Guid.NewGuid())));
    }

    private static OnePondy.UnitTests.Identity.TestDbContext TestDbContextFor() => OnePondy.UnitTests.Identity.TestDbContext.Create();
}
