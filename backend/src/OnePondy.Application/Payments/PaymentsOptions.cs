namespace OnePondy.Application.Payments;

/// <summary>Settings for online payments (spec Phase 7). Everything defaults to sandbox and to "off".</summary>
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    public const string SandboxMode = "Sandbox";
    public const string LiveMode = "Live";

    /// <summary><c>Sandbox</c> (the default) or <c>Live</c>. Live also needs <see cref="AllowLive"/> and live keys.</summary>
    public string Mode { get; set; } = SandboxMode;

    /// <summary>The deliberate switch for live money. Left false until the go-live checklist in docs/phase-7-plan.md is complete.</summary>
    public bool AllowLive { get; set; }

    /// <summary>How long a customer has to pay once a partner accepts a booking.</summary>
    public TimeSpan PaymentWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Provisional commission, for development and sandbox testing only. The real rate is a business
    /// decision still open (spec D21); settlement and statements are not built yet, so nothing uses this.
    /// </summary>
    public decimal CommissionPercent { get; set; } = 10m;

    /// <summary>Refunds stay off until the cancellation policy is approved (spec D22).</summary>
    public bool RefundsEnabled { get; set; }

    public RazorpayOptions Razorpay { get; set; } = new();
}

public sealed class RazorpayOptions
{
    public string? KeyId { get; set; }

    public string? KeySecret { get; set; }

    public string? WebhookSecret { get; set; }
}

/// <summary>Refuses to start with a payment configuration that could move real money by accident.</summary>
public static class PaymentsGuard
{
    private const string TestKeyPrefix = "rzp_test_";
    private const string LiveKeyPrefix = "rzp_live_";

    public static readonly TimeSpan MinimumPaymentWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumPaymentWindow = TimeSpan.FromHours(2);

    public static void EnsureSafe(PaymentsOptions options)
    {
        var isLive = string.Equals(options.Mode, PaymentsOptions.LiveMode, StringComparison.OrdinalIgnoreCase);
        if (!isLive && !string.Equals(options.Mode, PaymentsOptions.SandboxMode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Payments:Mode must be 'Sandbox' or 'Live'.");
        }

        if (options.PaymentWindow < MinimumPaymentWindow || options.PaymentWindow > MaximumPaymentWindow)
        {
            throw new InvalidOperationException(
                $"Payments:PaymentWindow must be between {MinimumPaymentWindow.TotalMinutes} and {MaximumPaymentWindow.TotalMinutes} minutes.");
        }

        if (options.CommissionPercent is < 0m or > 100m)
        {
            throw new InvalidOperationException("Payments:CommissionPercent must be between 0 and 100.");
        }

        var keyId = options.Razorpay.KeyId?.Trim();
        var hasLiveKey = keyId?.StartsWith(LiveKeyPrefix, StringComparison.Ordinal) == true;
        if (!string.IsNullOrEmpty(keyId) && !hasLiveKey && !keyId.StartsWith(TestKeyPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Payments:Razorpay:KeyId must be a Razorpay test key (rzp_test_...) or live key (rzp_live_...).");
        }

        if (hasLiveKey && !(isLive && options.AllowLive))
        {
            throw new InvalidOperationException(
                "A live Razorpay key is configured but live payments are not enabled. Live keys need Payments:Mode 'Live' and " +
                "Payments:AllowLive true, which stay off until legal review and a settlement test are complete. Use a rzp_test_ key.");
        }

        if (isLive && !options.AllowLive)
        {
            throw new InvalidOperationException("Payments:Mode 'Live' needs Payments:AllowLive true, which is not allowed yet.");
        }

        if (isLive && !hasLiveKey)
        {
            throw new InvalidOperationException("Payments:Mode 'Live' needs a live Razorpay key (rzp_live_...).");
        }
    }
}
