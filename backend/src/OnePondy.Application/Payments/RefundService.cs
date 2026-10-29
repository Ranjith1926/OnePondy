using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Payments;

/// <summary>What a cancellation would refund, and the payment it comes out of.</summary>
public sealed record RefundPlan(PaymentAttempt Attempt, decimal Amount);

public static class RefundErrors
{
    public static readonly AppError NotEnabled = new(
        "refunds_not_enabled",
        ErrorKind.Unavailable,
        "Cancelling a paid booking is not available yet. Contact support.");
}

/// <summary>
/// Refunds of paid bookings (spec D22, Phase 7). Deciding what is owed is separate from sending it: the decision is
/// saved with the booking's cancellation, then the gateway is asked, and a request that does not get through is
/// retried by <see cref="SendUnsentAsync"/> rather than lost.
/// </summary>
public sealed class RefundService(
    IOnePondyDbContext db,
    IPaymentGateway gateway,
    TimeProvider time,
    IOptions<PaymentsOptions> options,
    ILogger<RefundService> logger)
{
    /// <summary>A refund the gateway has not been told about is retried only once it is this old, so the sweep never races the request that just made it.</summary>
    public static readonly TimeSpan SweepAge = TimeSpan.FromMinutes(2);

    /// <summary>
    /// What cancelling this booking now would refund. <c>null</c> when nothing was paid online (not yet confirmed, free,
    /// or paid at the venue). Refused while refunds are switched off, so a paid booking is never cancelled with its money
    /// silently kept; the policy behind this rule is not approved yet (spec D22).
    /// </summary>
    public async Task<Result<RefundPlan?>> PlanCancellationAsync(Booking booking, RefundReason reason, CancellationToken cancellationToken)
    {
        if (booking.Status != BookingStatus.Confirmed || booking.TotalAmount <= 0m)
        {
            return Result<RefundPlan?>.Success(null);
        }

        // The payment that confirmed the booking is the first one captured; a later one would be a duplicate, refunded already.
        var paid = await db.PaymentAttempts
            .Where(a => a.BookingId == booking.Id && a.Status == PaymentAttemptStatus.Captured)
            .OrderBy(a => a.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (paid is null)
        {
            return Result<RefundPlan?>.Success(null);
        }

        if (!options.Value.RefundsEnabled)
        {
            return RefundErrors.NotEnabled;
        }

        var amount = RefundCalculator.Calculate(paid.Amount, reason, booking.CancellationPolicySnapshot, booking.StartAt, time.GetUtcNow());
        return Result<RefundPlan?>.Success(new RefundPlan(paid, amount));
    }

    /// <summary>
    /// Asks the gateway to send the money back. The refund's own id is the idempotency key, so asking again after a
    /// timeout returns the same refund instead of paying twice. If it cannot be sent, the refund stays pending without a
    /// gateway id and <see cref="SendUnsentAsync"/> or a repeat webhook sends it later.
    /// </summary>
    public async Task SendAsync(Refund refund, PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var result = await gateway.RefundAsync(
            new GatewayRefundRequest(attempt.ProviderPaymentId!, Money.ToPaise(refund.Amount), refund.Id.ToString("N")),
            cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogWarning("Refund {RefundId} for payment attempt {AttemptId} could not be sent to the gateway yet", refund.Id, attempt.Id);
            return;
        }

        refund.MarkInitiated(result.Value.RefundId);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sends refunds that were recorded but never reached the gateway. Returns how many it sent.</summary>
    public async Task<int> SendUnsentAsync(int batchSize, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - SweepAge;
        var unsent = await db.Refunds
            .Where(r => r.Status == RefundStatus.Pending && r.ProviderRefundId == null && r.RequestedAt < cutoff)
            .OrderBy(r => r.RequestedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var refund in unsent)
        {
            var attempt = await db.PaymentAttempts.SingleAsync(a => a.Id == refund.PaymentAttemptId, cancellationToken);
            await SendAsync(refund, attempt, cancellationToken);
            if (refund.ProviderRefundId is not null)
            {
                sent++;
            }
        }

        return sent;
    }
}
