using OnePondy.Application.Payments;
using OnePondy.Domain.Listings;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Domain;

public class RefundCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly CancellationPolicy Policy = new(24, 50, null);

    [Theory]
    [InlineData(RefundReason.Rejected)]
    [InlineData(RefundReason.PartnerCancelled)]
    [InlineData(RefundReason.LatePayment)]
    public void Anything_the_customer_did_not_cause_is_refunded_in_full(RefundReason reason)
    {
        // Even an hour before the start, when the customer's own cancellation would only get half back.
        Assert.Equal(1000m, RefundCalculator.Calculate(1000m, reason, Policy, Start, Start.AddHours(-1)));
    }

    [Fact]
    public void A_customer_cancelling_before_the_free_deadline_gets_everything_back()
    {
        Assert.Equal(1000m, RefundCalculator.Calculate(1000m, RefundReason.CustomerCancelled, Policy, Start, Start.AddHours(-25)));
    }

    [Fact]
    public void A_customer_cancelling_exactly_at_the_deadline_still_gets_everything_back()
    {
        Assert.Equal(1000m, RefundCalculator.Calculate(1000m, RefundReason.CustomerCancelled, Policy, Start, Start.AddHours(-24)));
    }

    [Fact]
    public void A_customer_cancelling_after_the_deadline_gets_the_listings_percentage()
    {
        Assert.Equal(500m, RefundCalculator.Calculate(1000m, RefundReason.CustomerCancelled, Policy, Start, Start.AddHours(-23)));
    }

    [Fact]
    public void The_percentage_is_rounded_to_whole_paise()
    {
        var policy = new CancellationPolicy(0, 33, null);

        Assert.Equal(330.00m, RefundCalculator.Calculate(999.99m, RefundReason.CustomerCancelled, policy, Start, Start.AddMinutes(1)));
    }

    [Fact]
    public void A_zero_percent_policy_refunds_nothing_after_the_deadline()
    {
        var policy = new CancellationPolicy(48, 0, null);

        Assert.Equal(0m, RefundCalculator.Calculate(1000m, RefundReason.CustomerCancelled, policy, Start, Start.AddHours(-2)));
    }

    [Fact]
    public void A_booking_with_no_start_time_is_treated_as_cancellable_for_free()
    {
        Assert.Equal(1000m, RefundCalculator.Calculate(1000m, RefundReason.CustomerCancelled, Policy, null, Start));
    }
}

public class PaymentDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_payment_attempt_starts_created_and_can_be_captured_once()
    {
        var attempt = PaymentAttempt.Create(Guid.NewGuid(), "order_1", 1000m, "INR", Now);

        attempt.MarkCaptured("pay_1", Now.AddMinutes(1));

        Assert.Equal(PaymentAttemptStatus.Captured, attempt.Status);
        Assert.Equal("pay_1", attempt.ProviderPaymentId);
        Assert.Throws<InvalidOperationException>(() => attempt.MarkCaptured("pay_2", Now));
        Assert.Throws<InvalidOperationException>(() => attempt.MarkFailed(Now));
    }

    [Fact]
    public void A_payment_attempt_must_be_for_a_positive_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentAttempt.Create(Guid.NewGuid(), "order_1", 0m, "INR", Now));
    }

    [Fact]
    public void A_ledger_line_cannot_be_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LedgerEntry.Create(Guid.NewGuid(), LedgerAccounts.GatewayClearing, 0m, null, "nothing", Now));
    }

    [Fact]
    public void Partner_accounts_are_named_per_business()
    {
        var business = Guid.NewGuid();

        Assert.Equal($"partner_payable:{business}", LedgerAccounts.PartnerPayable(business));
        Assert.Equal($"partner_paid:{business}", LedgerAccounts.PartnerPaid(business));
    }
}

public class PaymentsGuardTests
{
    private static PaymentsOptions Options(string? keyId = null, string mode = "Sandbox", bool allowLive = false) =>
        new() { Mode = mode, AllowLive = allowLive, Razorpay = new RazorpayOptions { KeyId = keyId } };

    [Fact]
    public void The_defaults_are_a_sandbox_with_a_thirty_minute_window_and_ten_percent_provisional_commission()
    {
        var options = new PaymentsOptions();

        PaymentsGuard.EnsureSafe(options);

        Assert.Equal("Sandbox", options.Mode);
        Assert.False(options.AllowLive);
        Assert.False(options.RefundsEnabled);
        Assert.Equal(TimeSpan.FromMinutes(30), options.PaymentWindow);
        Assert.Equal(10m, options.CommissionPercent);
    }

    [Fact]
    public void A_test_key_in_sandbox_mode_is_accepted()
    {
        PaymentsGuard.EnsureSafe(Options("rzp_test_abc123"));
    }

    [Fact]
    public void A_live_key_is_refused_in_sandbox_mode()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PaymentsGuard.EnsureSafe(Options("rzp_live_abc123")));

        Assert.Contains("live payments are not enabled", exception.Message);
    }

    [Fact]
    public void A_live_key_is_still_refused_in_live_mode_without_the_explicit_switch()
    {
        Assert.Throws<InvalidOperationException>(() => PaymentsGuard.EnsureSafe(Options("rzp_live_abc123", "Live", allowLive: false)));
    }

    [Fact]
    public void A_live_key_is_refused_when_the_switch_is_on_but_the_mode_is_sandbox()
    {
        Assert.Throws<InvalidOperationException>(() => PaymentsGuard.EnsureSafe(Options("rzp_live_abc123", "Sandbox", allowLive: true)));
    }

    [Fact]
    public void Live_mode_without_the_switch_is_refused_even_with_no_key()
    {
        Assert.Throws<InvalidOperationException>(() => PaymentsGuard.EnsureSafe(Options(null, "Live", allowLive: false)));
    }

    [Fact]
    public void Live_mode_needs_a_live_key()
    {
        Assert.Throws<InvalidOperationException>(() => PaymentsGuard.EnsureSafe(Options("rzp_test_abc123", "Live", allowLive: true)));
    }

    [Fact]
    public void Only_a_complete_deliberate_live_setup_is_allowed()
    {
        PaymentsGuard.EnsureSafe(Options("rzp_live_abc123", "Live", allowLive: true));
    }

    [Theory]
    [InlineData("not_a_razorpay_key")]
    [InlineData("RZP_TEST_abc")]
    public void An_unrecognised_key_is_refused(string key)
    {
        Assert.Throws<InvalidOperationException>(() => PaymentsGuard.EnsureSafe(Options(key)));
    }

    [Fact]
    public void An_unknown_mode_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => PaymentsGuard.EnsureSafe(Options(null, "Production")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(121)]
    public void A_payment_window_outside_five_minutes_to_two_hours_is_refused(int minutes)
    {
        var options = new PaymentsOptions { PaymentWindow = TimeSpan.FromMinutes(minutes) };

        Assert.Throws<InvalidOperationException>(() => PaymentsGuard.EnsureSafe(options));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_commission_outside_zero_to_one_hundred_is_refused(int percent)
    {
        var options = new PaymentsOptions { CommissionPercent = percent };

        Assert.Throws<InvalidOperationException>(() => PaymentsGuard.EnsureSafe(options));
    }
}
