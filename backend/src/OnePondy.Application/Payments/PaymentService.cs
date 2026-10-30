using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Bookings;
using OnePondy.Application.Common;
using OnePondy.Application.Food;
using OnePondy.Application.Notifications;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Food;
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

/// <summary>The same as <see cref="PaymentStartView"/>, for a food order. <c>OrderId</c> is the gateway's order, as for bookings.</summary>
public sealed record FoodPaymentStartView(
    Guid FoodOrderId,
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
    /// <summary>The booking or food order was waiting for this payment and is now confirmed.</summary>
    Confirmed,

    /// <summary>Nothing needed the money any more (cancelled, expired, rejected, or already paid); a full refund was raised.</summary>
    RefundRaised,

    /// <summary>This payment was recorded earlier; nothing changed.</summary>
    AlreadyRecorded,
}

public sealed record CaptureResult(CaptureOutcome Outcome, Guid? RefundId);

/// <summary>What asking the gateway about a booking or food order that is about to expire found.</summary>
public enum ExpiryCheck
{
    /// <summary>Nothing was paid, as far as the gateway says. The hold can be released.</summary>
    NotPaid,

    /// <summary>The gateway had a payment and it was applied, so it is no longer waiting. Do not expire it.</summary>
    Resolved,

    /// <summary>The gateway could not be asked, or what it said could not be applied. Do not guess: try again shortly.</summary>
    Unknown,
}

/// <summary>
/// Paying for a booking (spec Phase 7) or a food order (step 8.3). A customer starts a payment once the partner has accepted; the
/// result comes back from the gateway, and only that result confirms it (<see cref="RecordCaptureAsync"/>). A payment attempt belongs
/// to exactly one booking or one food order, and everything after the gateway order (webhooks, the check before expiry,
/// reconciliation, refunds) finds its way back through the attempt.
/// </summary>
public sealed class PaymentService(
    IOnePondyDbContext db,
    IPaymentGateway gateway,
    TimeProvider time,
    IOptions<PaymentsOptions> options,
    LedgerService ledger,
    RefundService refunds,
    NotificationOutbox notifications,
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

        if (booking.ExpiresAt is not { } expiresAt || time.GetUtcNow() >= expiresAt)
        {
            return PaymentErrors.WindowClosed;
        }

        var attempt = await OpenAttemptAsync(
            db.PaymentAttempts.Where(a => a.BookingId == bookingId),
            new GatewayOrderRequest(Money.ToPaise(booking.TotalAmount), booking.Currency, booking.Id.ToString("N"), booking.Id),
            (orderId, now) => PaymentAttempt.Create(booking.Id, orderId, booking.TotalAmount, booking.Currency, now),
            cancellationToken);
        return attempt.IsSuccess ? ToView(booking.Id, attempt.Value, expiresAt) : attempt.Error;
    }

    /// <summary>The same as <see cref="StartPaymentAsync"/>, for a food order the restaurant has accepted. Another customer's order is "not found".</summary>
    public async Task<Result<FoodPaymentStartView>> StartFoodOrderPaymentAsync(Guid orderId, Guid customerUserId, CancellationToken cancellationToken)
    {
        var order = await db.FoodOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null || order.CustomerUserId != customerUserId)
        {
            return OrderErrors.NotFound;
        }

        if (order.Status != FoodOrderStatus.AwaitingPayment || order.TotalAmount <= 0m)
        {
            return PaymentErrors.OrderNotDue;
        }

        if (order.ExpiresAt is not { } expiresAt || time.GetUtcNow() >= expiresAt)
        {
            return PaymentErrors.OrderWindowClosed;
        }

        // The receipt says it is food ("fo_" plus the order id, 35 characters, inside the gateway's 40); a booking's is its bare id.
        var attempt = await OpenAttemptAsync(
            db.PaymentAttempts.Where(a => a.FoodOrderId == orderId),
            new GatewayOrderRequest(Money.ToPaise(order.TotalAmount), order.Currency, "fo_" + order.Id.ToString("N"), null, order.Id),
            (gatewayOrderId, now) => PaymentAttempt.CreateForFoodOrder(order.Id, gatewayOrderId, order.TotalAmount, order.Currency, now),
            cancellationToken);
        return attempt.IsSuccess ? ToFoodView(order.Id, attempt.Value, expiresAt) : attempt.Error;
    }

    /// <summary>
    /// Applies a successful payment reported by the gateway (called by the verified webhook, step 7.3). Safe to repeat. If the booking
    /// or food order no longer wants the money (the customer cancelled while paying, the hold expired, the partner rejected, or a
    /// different payment already confirmed it), the payment is recorded and refunded in full.
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

        var now = time.GetUtcNow();
        var refund = attempt.FoodOrderId is { } orderId
            ? await ApplyToFoodOrderAsync(attempt, orderId, providerPaymentId, now, cancellationToken)
            : await ApplyToBookingAsync(attempt, attempt.BookingId!.Value, providerPaymentId, now, cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The booking or order (or the attempt) changed in the same instant, for example the customer cancelling while
            // this payment arrived. Nothing was saved; the gateway retries, and the retry sees the new state.
            db.ClearTracked();
            return PaymentErrors.Busy;
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // The same payment was applied on another request at the same moment: the ledger refuses to book it twice.
            // Nothing of ours was saved; the retry finds it already recorded.
            logger.LogWarning(ex, "Payment {AttemptId} was being applied elsewhere at the same time", attempt.Id);
            db.ClearTracked();
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
    public Task<ExpiryCheck> CheckBeforeExpiryAsync(Guid bookingId, CancellationToken cancellationToken) =>
        CheckGatewayAsync(db.PaymentAttempts.Where(a => a.BookingId == bookingId), cancellationToken);

    /// <summary>The same check as <see cref="CheckBeforeExpiryAsync"/>, before a food order waiting for payment is expired.</summary>
    public Task<ExpiryCheck> CheckFoodOrderBeforeExpiryAsync(Guid orderId, CancellationToken cancellationToken) =>
        CheckGatewayAsync(db.PaymentAttempts.Where(a => a.FoodOrderId == orderId), cancellationToken);

    /// <summary>
    /// The customer's app has just finished the gateway's checkout and asks "is it paid yet?". The answer is never the app's: this
    /// asks the gateway directly (the same check the Worker makes before expiring a booking) and applies a captured payment exactly
    /// as a webhook would. It is what makes a payment show up straight away when the webhook is slow or cannot reach the server.
    /// Returns nothing to say about the result; the caller reloads the booking. Another customer's booking is "not found".
    /// </summary>
    public async Task<AppError?> CheckNowAsync(Guid bookingId, Guid customerUserId, CancellationToken cancellationToken)
    {
        var owns = await db.Bookings.AsNoTracking().AnyAsync(b => b.Id == bookingId && b.CustomerUserId == customerUserId, cancellationToken);
        if (!owns)
        {
            return BookingErrors.NotFound;
        }

        var waiting = await db.Bookings.AsNoTracking().AnyAsync(b => b.Id == bookingId && b.Status == BookingStatus.AwaitingPayment, cancellationToken);
        if (waiting)
        {
            await CheckBeforeExpiryAsync(bookingId, cancellationToken);
        }

        return null;
    }

    /// <summary>The same as <see cref="CheckNowAsync"/>, for a food order. Another customer's order is "not found".</summary>
    public async Task<AppError?> CheckFoodOrderNowAsync(Guid orderId, Guid customerUserId, CancellationToken cancellationToken)
    {
        var order = await db.FoodOrders.AsNoTracking()
            .Where(o => o.Id == orderId && o.CustomerUserId == customerUserId)
            .Select(o => new { o.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return OrderErrors.NotFound;
        }

        if (order.Status == FoodOrderStatus.AwaitingPayment)
        {
            await CheckFoodOrderBeforeExpiryAsync(orderId, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// The gateway reports a refund as paid out. Safe to repeat. A booking or food order waiting on its refund (<c>refund_pending</c>)
    /// becomes <c>refunded</c> once none of its refunds are still outstanding; an expired one is already final, so only the refund
    /// record changes.
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

        var paidAttempt = await db.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == refund.PaymentAttemptId, cancellationToken);
        if (refund.FoodOrderId is { } orderId)
        {
            await FinishFoodRefundAsync(refund, paidAttempt, orderId, now, cancellationToken);
        }
        else
        {
            await FinishBookingRefundAsync(refund, paidAttempt, refund.BookingId!.Value, now, cancellationToken);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // A concurrency conflict, or the same refund being booked on another request at the same moment.
            logger.LogWarning(ex, "Refund {RefundId} was being recorded elsewhere at the same time", refund.Id);
            db.ClearTracked();
            return PaymentErrors.Busy;
        }

        return true;
    }

    /// <summary>The gateway reports a refund failed. It is recorded for follow-up; the booking or order stays <c>refund_pending</c>.</summary>
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

    /// <summary>
    /// The open gateway order among <paramref name="attempts"/>, or a new one. Two requests at the same instant cannot both open one
    /// (one open attempt per booking or order is a database rule); the loser's gateway order is left unused, which is harmless, and it
    /// gets the winner back.
    /// </summary>
    private async Task<Result<PaymentAttempt>> OpenAttemptAsync(
        IQueryable<PaymentAttempt> attempts,
        GatewayOrderRequest request,
        Func<string, DateTimeOffset, PaymentAttempt> create,
        CancellationToken cancellationToken)
    {
        var open = await FindOpenAttemptAsync(attempts, cancellationToken);
        if (open is not null)
        {
            return open;
        }

        var order = await gateway.CreateOrderAsync(request, cancellationToken);
        if (!order.IsSuccess)
        {
            return order.Error;
        }

        var attempt = create(order.Value.OrderId, time.GetUtcNow());
        db.PaymentAttempts.Add(attempt);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            db.PaymentAttempts.Remove(attempt);
            var winner = await FindOpenAttemptAsync(attempts, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return winner;
        }

        return attempt;
    }

    /// <summary>A payment for a booking: confirms it, or (when nothing wants it any more) records it for a full refund. Returns that refund.</summary>
    private async Task<Refund?> ApplyToBookingAsync(
        PaymentAttempt attempt, Guid bookingId, string providerPaymentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId, cancellationToken);
        var paidAlready = await db.PaymentAttempts.AnyAsync(
            a => a.BookingId == booking.Id && a.Id != attempt.Id && a.Status == PaymentAttemptStatus.Captured,
            cancellationToken);

        attempt.MarkCaptured(providerPaymentId, now);

        if (booking.Status == BookingStatus.AwaitingPayment && !paidAlready)
        {
            booking.ConfirmPayment(now);
            db.MarkAdded(booking.History[^1]);
            await ledger.PostCaptureAsync(booking, attempt, cancellationToken);
            await notifications.EnqueuePaymentConfirmedAsync(booking, cancellationToken);
            return null;
        }

        // Cancelled and rejected bookings move to refund_pending, as the spec's state table says. An expired
        // one is final, so only the refund record is raised.
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Rejected)
        {
            booking.RequestRefund(BookingActorType.System, null, now);
            db.MarkAdded(booking.History[^1]);
        }

        return await HoldForRefundAsync(attempt, now, cancellationToken);
    }

    /// <summary>
    /// A payment for a food order: the same rules as a booking. Orders send no notifications yet, so nothing is queued. The order's row
    /// version guards the save, so a cancellation at the same instant makes one of the two retry.
    /// </summary>
    private async Task<Refund?> ApplyToFoodOrderAsync(
        PaymentAttempt attempt, Guid orderId, string providerPaymentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var order = await db.FoodOrders.SingleAsync(o => o.Id == orderId, cancellationToken);
        var paidAlready = await db.PaymentAttempts.AnyAsync(
            a => a.FoodOrderId == order.Id && a.Id != attempt.Id && a.Status == PaymentAttemptStatus.Captured,
            cancellationToken);

        attempt.MarkCaptured(providerPaymentId, now);

        if (order.Status == FoodOrderStatus.AwaitingPayment && !paidAlready)
        {
            order.ConfirmPayment(now);
            db.MarkAdded(order.History[^1]);
            await ledger.PostFoodCaptureAsync(order, attempt, cancellationToken);
            return null;
        }

        if (order.Status is FoodOrderStatus.Cancelled or FoodOrderStatus.Rejected)
        {
            order.RequestRefund(FoodOrderActorType.System, null, now);
            db.MarkAdded(order.History[^1]);
        }

        return await HoldForRefundAsync(attempt, now, cancellationToken);
    }

    /// <summary>Money nothing wants (late, or a second payment): held for the customer and refunded in full, whether or not refunds are switched on.</summary>
    private async Task<Refund> HoldForRefundAsync(PaymentAttempt attempt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var refund = Refund.Of(attempt, attempt.Amount, RefundReason.LatePayment, now);
        db.Refunds.Add(refund);
        await ledger.PostHeldPaymentAsync(attempt, cancellationToken);
        return refund;
    }

    private async Task FinishBookingRefundAsync(Refund refund, PaymentAttempt paidAttempt, Guid bookingId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId, cancellationToken);
        await ledger.PostRefundAsync(refund, booking, paidAttempt, cancellationToken);
        notifications.EnqueueRefundProcessed(booking);
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
    }

    private async Task FinishFoodRefundAsync(Refund refund, PaymentAttempt paidAttempt, Guid orderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var order = await db.FoodOrders.SingleAsync(o => o.Id == orderId, cancellationToken);
        await ledger.PostFoodRefundAsync(refund, paidAttempt, cancellationToken);
        if (order.Status == FoodOrderStatus.RefundPending)
        {
            var othersOutstanding = await db.Refunds.AnyAsync(
                r => r.FoodOrderId == order.Id && r.Id != refund.Id && r.Status == RefundStatus.Pending,
                cancellationToken);
            if (!othersOutstanding)
            {
                order.MarkRefunded(now);
                db.MarkAdded(order.History[^1]);
            }
        }
    }

    private async Task<ExpiryCheck> CheckGatewayAsync(IQueryable<PaymentAttempt> attemptsFor, CancellationToken cancellationToken)
    {
        var attempts = await attemptsFor.AsNoTracking()
            .Where(a => a.Status != PaymentAttemptStatus.Captured)
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

            // Wrong amount, or the booking or order changed at that instant. Neither is safe to settle by expiring.
            logger.LogWarning("A payment found at the gateway for attempt {AttemptId} could not be applied: {Code}", attempt.Id, applied.Error.Code);
            unsure = true;
        }

        return unsure ? ExpiryCheck.Unknown : ExpiryCheck.NotPaid;
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

    private static Task<PaymentAttempt?> FindOpenAttemptAsync(IQueryable<PaymentAttempt> attempts, CancellationToken cancellationToken) =>
        attempts.AsNoTracking()
            .Where(a => a.Status == PaymentAttemptStatus.Created)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private PaymentStartView ToView(Guid bookingId, PaymentAttempt attempt, DateTimeOffset expiresAt) => new(
        bookingId,
        attempt.Id,
        "razorpay",
        _options.Mode.ToLowerInvariant(),
        _options.Razorpay.KeyId ?? string.Empty,
        attempt.ProviderOrderId,
        attempt.Amount,
        Money.ToPaise(attempt.Amount),
        attempt.Currency,
        expiresAt);

    private FoodPaymentStartView ToFoodView(Guid orderId, PaymentAttempt attempt, DateTimeOffset expiresAt) => new(
        orderId,
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
