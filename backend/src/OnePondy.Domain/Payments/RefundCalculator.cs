using OnePondy.Domain.Listings;

namespace OnePondy.Domain.Payments;

/// <summary>
/// The refund rule proposed in spec D22. Provisional until the cancellation policy is approved, and not
/// used by any live flow yet: nothing refunds money until Phase 7 step 7.5 wires it in.
/// </summary>
public static class RefundCalculator
{
    /// <summary>
    /// Everything the customer did not cause is refunded in full. A customer's own cancellation is refunded
    /// in full up to the listing's free-cancellation deadline, and by the listing's refund percentage after it.
    /// </summary>
    public static decimal Calculate(
        decimal capturedAmount,
        RefundReason reason,
        CancellationPolicy policy,
        DateTimeOffset? startAt,
        DateTimeOffset cancelledAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capturedAmount);

        if (reason != RefundReason.CustomerCancelled)
        {
            return capturedAmount;
        }

        if (startAt is not { } start || cancelledAt <= start - TimeSpan.FromHours(policy.FreeCancellationHours))
        {
            return capturedAmount;
        }

        return Math.Round(capturedAmount * policy.RefundPercentage / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
