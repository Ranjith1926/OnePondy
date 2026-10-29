using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Bookings;
using OnePondy.Application.Common;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Payments;

/// <summary>What the app needs to open the gateway's checkout. The key id is public; the secret never leaves the server.</summary>
public sealed record PaymentStartView(
    Guid BookingId,
    Guid AttemptId,
    string Provider,
    string Mode,
    string KeyId,
    string OrderId,
    decimal Amount,
    long AmountPaise,
    string Currency,
    DateTimeOffset PaymentExpiresAt);

public enum CaptureOutcome
{
    /// <summary>The booking was waiting for this payment and is now confirmed.</summary>
    Confirmed,

    /// <summary>Nothing needed the money any more (cancelled, expired, rejected, or already paid); a full refund was raised.</summary>
    RefundRaised,

    /// <summary>This payment was recorded earlier; nothing changed.</summary>
    AlreadyRecorded,
}

public sealed record CaptureResult(CaptureOutcome Outcome, Guid? RefundId);

/// <summary>What asking the gateway about a booking that is about to expire found.</summary>
public enum ExpiryCheck
{
    /// <summary>Nothing was paid, as far as the gateway says. The hold can be released.</summary>
    NotPaid,

    /// <summary>The gateway had a payment and it was applied, so the booking is no longer waiting. Do not expire it.</summary>
    Resolved,

    /// <summary>The gateway could not be asked, or what it said could not be applied. Do not guess: try again shortly.</summary>
    Unknown,
}

/// <summary>
/// Paying for a booking (spec Phase 7). A customer starts a payment once a partner has accepted; the result
/// comes back from the gateway, and only that result confirms the booking (<see cref="RecordCaptureAsync"/>).
/// </summary>
public sealed class PaymentService(
    IOnePondyDbContext db,
    IPaymentGateway gateway,
    TimeProvider time,
    IOptions<PaymentsOptions> options,
    LedgerService ledger,
    RefundService refunds,
    ILogger<PaymentService> logger)
{
    private readonly PaymentsOptions _options = options.Value;

    /// <summary>
    /// Creates, or returns the one already open, gateway order for a booking that is waiting for payment. Asking
    /// twice, including at the same instant, gives the same order, so a customer is never offered two ways to pay.
    /// </summary>
    public async Task<Result<PaymentStartView>> StartPaymentAsync(Guid bookingId, Guid customerUserId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null || booking.CustomerUserId != customerUserId)
        {
            return BookingErrors.NotFound;
        }

        if (booking.Status != BookingStatus.AwaitingPayment || booking.TotalAmount <= 0m)
        {
            return PaymentErrors.NotDue;
        }

        var now = time.GetUtcNow();
        if (booking.ExpiresAt is not { } expiresAt || now >= expiresAt)
        {
            return PaymentErrors.WindowClosed;
        }

        var open = await FindOpenAttemptAsync(bookingId, cancellationToken);
        if (open is not null)
        {
            return ToView(booking, open, expiresAt);
        }

        var order = await gateway.CreateOrderAsync(
            new GatewayOrderRequest(Money.ToPaise(booking.TotalAmount), booking.Currency, booking.Id.ToString("N"), booking.Id),
            cancellationToken);
        if (!order.IsSuccess)
        {
            return order.Error;
        }

        var attempt = PaymentAttempt.Create(booking.Id, order.Value.OrderId, booking.TotalAmount, booking.Currency, now);
        db.PaymentAttempts.Add(attempt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A parallel request opened an order first (one open attempt per booking is a database rule). Ours is
            // left unused at the gateway, which is harmless; hand back the winner.
            db.PaymentAttempts.Remove(attempt);
            var winner = await FindOpenAttemptAsync(bookingId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return ToView(booking, winner, expiresAt);
        }

        return ToView(booking, attempt, expiresAt);
    }

    /// <summary>
    /// Applies a successful payment reported by the gateway (called by the verified webhook, step 7.3). Safe to
    /// repeat. If the booking no longer wants the money (the customer cancelled while paying, the hold expired, the
    /// partner rejected, or a different payment already confirmed it), the payment is recorded and refunded in full.
    /// </summary>
    public async Task<Result<CaptureResult>> RecordCaptureAsync(
        string providerOrderId,
        string providerPaymentId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(a => a.ProviderOrderId == providerOrderId, cancellationToken);
        if (attempt is null)
        {
            return PaymentErrors.UnknownOrder;
        }

        if (attempt.Status == PaymentAttemptStatus.Captured)
        {
            if (attempt.ProviderPaymentId != providerPaymentId)
            {
                return PaymentErrors.AlreadyCaptured;
            }

            // A repeat delivery. If the first run stopped before the refund reached the gateway, finish that now.
            await RetryUnsentRefundsAsync(attempt, cancellationToken);
            return new CaptureResult(CaptureOutcome.AlreadyRecorded, null);
        }

        if (amount != attempt.Amount)
        {
            logger.LogWarning("Payment {AttemptId} reported {Reported} but {Expected} is due", attempt.Id, amount, attempt.Amount);
            return PaymentErrors.AmountMismatch;
        }

        var booking = await db.Bookings.SingleAsync(b => b.Id == attempt.BookingId, cancellationToken);
        var now = time.GetUtcNow();
        var paidAlready = await db.PaymentAttempts.AnyAsync(
            a => a.BookingId == booking.Id && a.Id != attempt.Id && a.Status == PaymentAttemptStatus.Captured,
            cancellationToken);

        attempt.MarkCaptured(providerPaymentId, now);

        Refund? refund = null;
        if (booking.Status == BookingStatus.AwaitingPayment && !paidAlready)
        {
            booking.ConfirmPayment(now);
            db.MarkAdded(booking.History[^1]);
            await ledger.PostCaptureAsync(booking, attempt, cancellationToken);
        }
        else
        {
            // Cancelled and rejected bookings move to refund_pending, as the spec's state table says. An expired
            // one is final, so only the refund record is raised.
            if (booking.Status is BookingStatus.Cancelled or BookingStatus.Rejected)
            {
                booking.RequestRefund(BookingActorType.System, null, now);
                db.MarkAdded(booking.History[^1]);
            }

            refund = Refund.Request(attempt.Id, booking.Id, attempt.Amount, RefundReason.LatePayment, now);
            db.Refunds.Add(refund);
            await ledger.PostHeldPaymentAsync(booking, attempt, cancellationToken);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The booking (or the attempt) changed in the same instant, for example the customer cancelling while
            // this payment arrived. Nothing was saved; the gateway retries, and the retry sees the new state.
            return PaymentErrors.Busy;
        }
        catch (DbUpdateException exception)
        {
            // The same payment was applied on another request at the same moment: the ledger refuses to book it twice.
            // Nothing of ours was saved; the retry finds it already recorded.
            logger.LogWarning(exception, "Payment {AttemptId} was being applied elsewhere at the same time", attempt.Id);
            return PaymentErrors.Busy;
        }

        if (refund is null)
        {
            return new CaptureResult(CaptureOutcome.Confirmed, null);
        }

        await refunds.SendAsync(refund, attempt, cancellationToken);
        return new CaptureResult(CaptureOutcome.RefundRaised, refund.Id);
    }

    /// <summary>
    /// Before a hold that was waiting for payment is released, asks the gateway whether it was paid after all (a
    /// webhook can be late or lost). A payment found is applied exactly as a webhook would, so the customer who paid
    /// at the last moment keeps their booking instead of being refunded. If the gateway cannot answer, the answer is
    /// <see cref="ExpiryCheck.Unknown"/> and the caller waits rather than releasing seats someone may have paid for.
    /// </summary>
    public async Task<ExpiryCheck> CheckBeforeExpiryAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var attempts = await db.PaymentAttempts.AsNoTracking()
            .Where(a => a.BookingId == bookingId && a.Status != PaymentAttemptStatus.Captured)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var unsure = false;
        foreach (var attempt in attempts)
        {
            var found = await gateway.GetOrderPaymentsAsync(attempt.ProviderOrderId, cancellationToken);
            if (!found.IsSuccess)
            {
                unsure = true;
                continue;
            }

            var paid = found.Value.FirstOrDefault(p => string.Equals(p.Status, "captured", StringComparison.OrdinalIgnoreCase));
            if (paid is null)
            {
                continue;
            }

            if (!string.Equals(paid.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("Order {AttemptId} was paid in {Currency}, not {Expected}; it needs a manual check", attempt.Id, paid.Currency, attempt.Currency);
                unsure = true;
                continue;
            }

            var applied = await RecordCaptureAsync(attempt.ProviderOrderId, paid.PaymentId, paid.AmountPaise / 100m, cancellationToken);
            if (applied.IsSuccess)
            {
                return ExpiryCheck.Resolved;
            }

            // Wrong amount, or the booking changed at that instant. Neither is safe to settle by expiring.
            logger.LogWarning("A payment found at the gateway for attempt {AttemptId} could not be applied: {Code}", attempt.Id, applied.Error.Code);
            unsure = true;
        }

        return unsure ? ExpiryCheck.Unknown : ExpiryCheck.NotPaid;
    }

    /// <summary>
    /// The gateway reports a refund as paid out. Safe to repeat. A booking waiting on its refund
    /// (<c>refund_pending</c>) becomes <c>refunded</c> once none of its refunds are still outstanding; an expired
    /// booking is already final, so only the refund record changes.
    /// </summary>
    public async Task<Result<bool>> RecordRefundProcessedAsync(string providerRefundId, CancellationToken cancellationToken)
    {
        var refund = await db.Refunds.SingleOrDefaultAsync(r => r.ProviderRefundId == providerRefundId, cancellationToken);
        if (refund is null)
        {
            return PaymentErrors.UnknownRefund;
        }

        if (refund.Status == RefundStatus.Processed)
        {
            return true;
        }

        var now = time.GetUtcNow();
        refund.MarkProcessed(providerRefundId, now);

        var booking = await db.Bookings.SingleAsync(b => b.Id == refund.BookingId, cancellationToken);
        var paidAttempt = await db.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == refund.PaymentAttemptId, cancellationToken);
        await ledger.PostRefundAsync(refund, booking, paidAttempt, cancellationToken);
        if (booking.Status == BookingStatus.RefundPending)
        {
            var othersOutstanding = await db.Refunds.AnyAsync(
                r => r.BookingId == booking.Id && r.Id != refund.Id && r.Status == RefundStatus.Pending,
                cancellationToken);
            if (!othersOutstanding)
            {
                booking.MarkRefunded(now);
                db.MarkAdded(booking.History[^1]);
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // A concurrency conflict, or the same refund being booked on another request at the same moment.
            logger.LogWarning(exception, "Refund {RefundId} was being recorded elsewhere at the same time", refund.Id);
            return PaymentErrors.Busy;
        }

        return true;
    }

    /// <summary>The gateway reports a refund failed. It is recorded for follow-up; the booking stays <c>refund_pending</c>.</summary>
    public async Task<Result<bool>> RecordRefundFailedAsync(string providerRefundId, CancellationToken cancellationToken)
    {
        var refund = await db.Refunds.SingleOrDefaultAsync(r => r.ProviderRefundId == providerRefundId, cancellationToken);
        if (refund is null)
        {
            return PaymentErrors.UnknownRefund;
        }

        if (refund.Status == RefundStatus.Pending)
        {
            refund.MarkFailed(time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Refund {RefundId} failed at the gateway and needs follow-up", refund.Id);
        }

        return true;
    }

    private async Task RetryUnsentRefundsAsync(PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var unsent = await db.Refunds
            .Where(r => r.PaymentAttemptId == attempt.Id && r.Status == RefundStatus.Pending && r.ProviderRefundId == null)
            .ToListAsync(cancellationToken);
        foreach (var refund in unsent)
        {
            await refunds.SendAsync(refund, attempt, cancellationToken);
        }
    }

    private Task<PaymentAttempt?> FindOpenAttemptAsync(Guid bookingId, CancellationToken cancellationToken) =>
        db.PaymentAttempts.AsNoTracking()
            .Where(a => a.BookingId == bookingId && a.Status == PaymentAttemptStatus.Created)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private PaymentStartView ToView(Booking booking, PaymentAttempt attempt, DateTimeOffset expiresAt) => new(
        booking.Id,
        attempt.Id,
        "razorpay",
        _options.Mode.ToLowerInvariant(),
        _options.Razorpay.KeyId ?? string.Empty,
        attempt.ProviderOrderId,
        attempt.Amount,
        Money.ToPaise(attempt.Amount),
        attempt.Currency,
        expiresAt);
}
