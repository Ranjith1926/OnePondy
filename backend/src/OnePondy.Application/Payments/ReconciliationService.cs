using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OnePondy.Application.Common;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Payments;

/// <param name="Checked">Things looked at (orders, refunds, payments).</param>
/// <param name="Raised">New problems recorded for a person.</param>
/// <param name="AutoResolved">Differences the system fixed by the same safe path a webhook takes, and recorded.</param>
/// <param name="GatewayReachable">False when the gateway could not be asked, so the gateway checks did not run.</param>
public sealed record ReconciliationSummary(int Checked, int Raised, int AutoResolved, bool GatewayReachable);

/// <summary>
/// Compares our payment records with the gateway's and lists the differences for a person (spec Phase 7: "reconciliation
/// finds and resolves failed or delayed callbacks"). Webhooks are the normal path; this is the safety net for the ones
/// that are late or lost. It does not fix by guessing: the only things it applies itself are a payment or refund the
/// gateway says is complete, applied by exactly the path a webhook takes and recorded as auto-resolved so a lost-webhook
/// problem is still visible. Everything else becomes an open exception.
/// </summary>
public sealed class ReconciliationService(
    IOnePondyDbContext db,
    IPaymentGateway gateway,
    PaymentService payments,
    TimeProvider time,
    ILogger<ReconciliationService> logger)
{
    /// <summary>Orders newer than this are asked about on every frequent run; older ones are covered by the daily comparison.</summary>
    public static readonly TimeSpan RecentOrderWindow = TimeSpan.FromHours(2);

    public const int MaxLookupsPerRun = 100;

    /// <summary>A refund the gateway accepted is asked about once it is this old.</summary>
    public static readonly TimeSpan RefundCheckAge = TimeSpan.FromMinutes(10);

    /// <summary>A refund still not at the gateway after this long (the sweep tries from two minutes) needs a person.</summary>
    public static readonly TimeSpan RefundNotSentAge = TimeSpan.FromMinutes(15);

    /// <summary>A refund the gateway still calls pending after this long needs a person.</summary>
    public static readonly TimeSpan RefundStuckAge = TimeSpan.FromHours(6);

    /// <summary>A webhook that is stored but unfinished this long after it arrived has not been redelivered.</summary>
    public static readonly TimeSpan EventUnprocessedAge = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan EventLookback = TimeSpan.FromDays(30);

    public static readonly TimeSpan LedgerLookback = TimeSpan.FromDays(7);

    /// <summary>The gateway list reaches this far each side of a day, so a payment made just before midnight and recorded just after is still matched.</summary>
    public static readonly TimeSpan DailyMargin = TimeSpan.FromHours(6);

    /// <summary>A day is reconciled once it has been over for this long, so its late webhooks have had time to arrive.</summary>
    public static readonly TimeSpan DailyReadyAfter = TimeSpan.FromHours(3);

    public const int MaxCatchUpDays = 7;

    private sealed class Tally
    {
        public int Checked;
        public int Raised;
        public int AutoResolved;
        public bool GatewayFailed;

        public ReconciliationSummary ToSummary() => new(Checked, Raised, AutoResolved, !GatewayFailed);
    }

    // --- Every few minutes ---

    /// <summary>The checks that are cheap and time sensitive: recent orders, refunds, webhooks and ledger entries.</summary>
    public async Task<ReconciliationSummary> RunFrequentChecksAsync(CancellationToken cancellationToken)
    {
        var tally = new Tally();
        await CheckRecentOrdersAsync(tally, cancellationToken);
        await CheckRefundsAsync(tally, cancellationToken);
        await CheckWebhooksAsync(tally, cancellationToken);
        await CheckLedgerAsync(tally, cancellationToken);
        return tally.ToSummary();
    }

    /// <summary>A customer may have paid while the webhook was lost: ask the gateway about orders that are not recorded as paid.</summary>
    private async Task CheckRecentOrdersAsync(Tally tally, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - RecentOrderWindow;
        var attempts = await db.PaymentAttempts.AsNoTracking()
            .Where(a => a.Status != PaymentAttemptStatus.Captured && a.CreatedAt >= since)
            .OrderByDescending(a => a.CreatedAt)
            .Take(MaxLookupsPerRun)
            .ToListAsync(cancellationToken);

        foreach (var attempt in attempts)
        {
            var found = await gateway.GetOrderPaymentsAsync(attempt.ProviderOrderId, cancellationToken);
            if (!found.IsSuccess)
            {
                // The gateway is not answering: stop rather than hammer it, and try again next time.
                tally.GatewayFailed = true;
                return;
            }

            tally.Checked++;
            var paid = found.Value.FirstOrDefault(p => string.Equals(p.Status, "captured", StringComparison.OrdinalIgnoreCase));
            if (paid is not null)
            {
                await ApplyGatewayPaymentAsync(tally, attempt, paid.PaymentId, paid.AmountPaise, cancellationToken);
            }
        }
    }

    /// <summary>Applies a payment the gateway reports as captured and we do not have, the way a webhook would.</summary>
    private async Task ApplyGatewayPaymentAsync(Tally tally, PaymentAttempt attempt, string paymentId, long amountPaise, CancellationToken cancellationToken)
    {
        var applied = await payments.RecordCaptureAsync(attempt.ProviderOrderId, paymentId, amountPaise / 100m, cancellationToken);
        if (applied.IsSuccess)
        {
            await RecordAutoResolvedAsync(
                tally,
                ReconciliationKind.PaymentAppliedWithoutWebhook,
                $"payment_applied_without_webhook:{attempt.Id}",
                $"Payment {paymentId} was found at the gateway with no webhook and was applied ({applied.Value.Outcome}).",
                "Applied from the gateway's own record, by the same path a webhook takes.",
                bookingId: attempt.BookingId,
                paymentAttemptId: attempt.Id,
                providerReference: paymentId,
                amount: attempt.Amount);
            return;
        }

        if (applied.Error.Code == PaymentErrors.Busy.Code)
        {
            // Changed at the same instant elsewhere; nothing was saved. The next run sees the new state.
            db.ClearTracked();
            return;
        }

        db.ClearTracked();
        await RaiseAsync(
            tally,
            ReconciliationKind.PaymentMismatch,
            $"payment_mismatch:{attempt.Id}",
            $"The gateway reports payment {paymentId} for {amountPaise / 100m:0.00}, but this order is for {attempt.Amount:0.00} ({applied.Error.Code}). It was not applied.",
            bookingId: attempt.BookingId,
            paymentAttemptId: attempt.Id,
            providerReference: paymentId,
            expectedAmount: attempt.Amount,
            actualAmount: amountPaise / 100m);
    }

    private async Task CheckRefundsAsync(Tally tally, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        // Refunds the gateway accepted and has not yet confirmed to us.
        var waiting = await db.Refunds.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Pending && r.ProviderRefundId != null && r.RequestedAt < now - RefundCheckAge)
            .OrderBy(r => r.RequestedAt)
            .Take(MaxLookupsPerRun)
            .ToListAsync(cancellationToken);
        foreach (var refund in waiting)
        {
            var detail = await gateway.GetRefundAsync(refund.ProviderRefundId!, cancellationToken);
            if (!detail.IsSuccess)
            {
                tally.GatewayFailed = true;
                break;
            }

            tally.Checked++;
            switch (detail.Value.Status.ToLowerInvariant())
            {
                case "processed":
                    var done = await payments.RecordRefundProcessedAsync(refund.ProviderRefundId!, cancellationToken);
                    if (done.IsSuccess)
                    {
                        await RecordAutoResolvedAsync(
                            tally,
                            ReconciliationKind.RefundAppliedWithoutWebhook,
                            $"refund_applied_without_webhook:{refund.Id}",
                            $"Refund {refund.ProviderRefundId} was paid out at the gateway with no webhook and was applied.",
                            "Applied from the gateway's own record, by the same path a webhook takes.",
                            bookingId: refund.BookingId,
                            refundId: refund.Id,
                            providerReference: refund.ProviderRefundId,
                            amount: refund.Amount);
                    }
                    else
                    {
                        db.ClearTracked();
                    }

                    break;

                case "failed":
                    await payments.RecordRefundFailedAsync(refund.ProviderRefundId!, cancellationToken);
                    await RaiseRefundFailedAsync(tally, refund, cancellationToken);
                    break;

                default:
                    if (refund.RequestedAt < now - RefundStuckAge)
                    {
                        await RaiseAsync(
                            tally,
                            ReconciliationKind.RefundStuck,
                            $"refund_stuck:{refund.Id}",
                            $"Refund {refund.ProviderRefundId} of {refund.Amount:0.00} has been pending at the gateway since {refund.RequestedAt:u}.",
                            bookingId: refund.BookingId,
                            refundId: refund.Id,
                            providerReference: refund.ProviderRefundId,
                            actualAmount: refund.Amount);
                    }

                    break;
            }
        }

        // Refunds the gateway told us failed (by webhook), not yet listed.
        var failed = await db.Refunds.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Failed && r.RequestedAt >= now - EventLookback)
            .Take(MaxLookupsPerRun)
            .ToListAsync(cancellationToken);
        foreach (var refund in failed)
        {
            await RaiseRefundFailedAsync(tally, refund, cancellationToken);
        }

        // Refunds that never reached the gateway, though the sweep has been trying.
        var unsent = await db.Refunds.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Pending && r.ProviderRefundId == null && r.RequestedAt < now - RefundNotSentAge)
            .Take(MaxLookupsPerRun)
            .ToListAsync(cancellationToken);
        foreach (var refund in unsent)
        {
            await RaiseAsync(
                tally,
                ReconciliationKind.RefundNotSent,
                $"refund_not_sent:{refund.Id}",
                $"Refund of {refund.Amount:0.00} has not reached the gateway since {refund.RequestedAt:u}.",
                bookingId: refund.BookingId,
                refundId: refund.Id,
                actualAmount: refund.Amount);
        }
    }

    private Task RaiseRefundFailedAsync(Tally tally, Refund refund, CancellationToken cancellationToken) => RaiseAsync(
        tally,
        ReconciliationKind.RefundFailed,
        $"refund_failed:{refund.Id}",
        $"Refund of {refund.Amount:0.00} failed at the gateway; the customer has not been paid back.",
        bookingId: refund.BookingId,
        refundId: refund.Id,
        providerReference: refund.ProviderRefundId,
        actualAmount: refund.Amount,
        cancellationToken: cancellationToken);

    /// <summary>Webhooks that were stored but could not be applied, or were never finished.</summary>
    private async Task CheckWebhooksAsync(Tally tally, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        var withErrors = await db.GatewayEvents.AsNoTracking()
            .Where(e => e.Error != null && e.ReceivedAt >= now - EventLookback)
            .OrderByDescending(e => e.ReceivedAt)
            .Take(MaxLookupsPerRun)
            .ToListAsync(cancellationToken);
        foreach (var gatewayEvent in withErrors)
        {
            await RaiseAsync(
                tally,
                ReconciliationKind.WebhookNeedsAttention,
                $"webhook_needs_attention:{gatewayEvent.Id}",
                $"Webhook {gatewayEvent.EventType} ({gatewayEvent.ProviderEventId}) could not be applied: {gatewayEvent.Error}.",
                gatewayEventId: gatewayEvent.Id,
                providerReference: gatewayEvent.ProviderEventId,
                cancellationToken: cancellationToken);
        }

        var unfinished = await db.GatewayEvents.AsNoTracking()
            .Where(e => e.ProcessedAt == null && e.ReceivedAt < now - EventUnprocessedAge && e.ReceivedAt >= now - EventLookback)
            .OrderBy(e => e.ReceivedAt)
            .Take(MaxLookupsPerRun)
            .ToListAsync(cancellationToken);
        foreach (var gatewayEvent in unfinished)
        {
            await RaiseAsync(
                tally,
                ReconciliationKind.WebhookUnprocessed,
                $"webhook_unprocessed:{gatewayEvent.Id}",
                $"Webhook {gatewayEvent.EventType} ({gatewayEvent.ProviderEventId}) arrived at {gatewayEvent.ReceivedAt:u} and was never finished.",
                gatewayEventId: gatewayEvent.Id,
                providerReference: gatewayEvent.ProviderEventId,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>Every payment and refund the system recorded as done should have its entries in the ledger.</summary>
    private async Task CheckLedgerAsync(Tally tally, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - LedgerLookback;

        var captured = await db.PaymentAttempts.AsNoTracking()
            .Where(a => a.Status == PaymentAttemptStatus.Captured && a.UpdatedAt >= since)
            .Take(500)
            .ToListAsync(cancellationToken);
        var captureTransactions = captured.ToDictionary(a => LedgerService.TransactionId(LedgerService.CaptureKey(a.Id)));
        var capturePosted = await PostedAsync(captureTransactions.Keys, cancellationToken);
        foreach (var (transaction, attempt) in captureTransactions.Where(kv => !capturePosted.Contains(kv.Key)))
        {
            _ = transaction;
            await RaiseAsync(
                tally,
                ReconciliationKind.LedgerProblem,
                $"ledger_missing:capture:{attempt.Id}",
                $"Payment {attempt.ProviderPaymentId} of {attempt.Amount:0.00} is recorded as received but has no ledger entries.",
                bookingId: attempt.BookingId,
                paymentAttemptId: attempt.Id,
                providerReference: attempt.ProviderPaymentId,
                actualAmount: attempt.Amount,
                cancellationToken: cancellationToken);
        }

        var processed = await db.Refunds.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Processed && r.ProcessedAt >= since)
            .Take(500)
            .ToListAsync(cancellationToken);
        var refundTransactions = processed.ToDictionary(r => LedgerService.TransactionId(LedgerService.RefundKey(r.Id)));
        var refundPosted = await PostedAsync(refundTransactions.Keys, cancellationToken);
        foreach (var (_, refund) in refundTransactions.Where(kv => !refundPosted.Contains(kv.Key)))
        {
            await RaiseAsync(
                tally,
                ReconciliationKind.LedgerProblem,
                $"ledger_missing:refund:{refund.Id}",
                $"Refund {refund.ProviderRefundId} of {refund.Amount:0.00} is recorded as paid but has no ledger entries.",
                bookingId: refund.BookingId,
                refundId: refund.Id,
                providerReference: refund.ProviderRefundId,
                actualAmount: refund.Amount,
                cancellationToken: cancellationToken);
        }
    }

    private async Task<HashSet<Guid>> PostedAsync(IEnumerable<Guid> transactions, CancellationToken cancellationToken)
    {
        var wanted = transactions.ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return [.. await db.LedgerEntries.AsNoTracking().Where(e => wanted.Contains(e.TransactionId)).Select(e => e.TransactionId).Distinct().ToListAsync(cancellationToken)];
    }

    // --- Once a day ---

    /// <summary>
    /// Reconciles every complete day since the last one done (at most a week at a time), in order. A day whose
    /// gateway check could not run is left for next time rather than skipped. Returns how many days were completed.
    /// </summary>
    public async Task<int> RunDueDailyChecksAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.PaymentsReconciledThrough, cancellationToken);
        var lastDone = setting is not null && DateOnly.TryParseExact(setting.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : today.AddDays(-2); // never run: start with yesterday, the most recent complete day

        var completed = 0;
        for (var day = lastDone.AddDays(1); completed < MaxCatchUpDays && day < today; day = day.AddDays(1))
        {
            var readyAt = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) + DailyReadyAfter;
            if (now < readyAt)
            {
                break;
            }

            var summary = await RunDailyAsync(day, cancellationToken);
            if (!summary.GatewayReachable)
            {
                break;
            }

            var value = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (setting is null)
            {
                setting = new AppSetting(AppSetting.PaymentsReconciledThrough, value);
                setting.ChangeBySystem(value, now);
                db.AppSettings.Add(setting);
            }
            else
            {
                setting.ChangeBySystem(value, now);
            }

            await db.SaveChangesAsync(cancellationToken);
            completed++;
            logger.LogInformation(
                "Payment reconciliation for {Day}: {Checked} payments checked, {Raised} new problems, {AutoResolved} fixed from the gateway",
                value,
                summary.Checked,
                summary.Raised,
                summary.AutoResolved);
        }

        return completed;
    }

    /// <summary>
    /// Compares one UTC day of the gateway's payments with ours, both ways: money the gateway shows that we do not (or
    /// disagree about), and money we recorded that the gateway does not show. Read-only at the gateway.
    /// </summary>
    public async Task<ReconciliationSummary> RunDailyAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var tally = new Tally();
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        var listed = await gateway.ListPaymentsAsync(dayStart - DailyMargin, dayEnd + DailyMargin, cancellationToken);
        if (!listed.IsSuccess)
        {
            tally.GatewayFailed = true;
            return tally.ToSummary();
        }

        var received = listed.Value.Payments
            .Where(p => string.Equals(p.Status, "captured", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Status, "refunded", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // The gateway's payments against our records.
        foreach (var record in received)
        {
            tally.Checked++;
            var attempt = record.OrderId is null
                ? null
                : await db.PaymentAttempts.AsNoTracking().FirstOrDefaultAsync(a => a.ProviderOrderId == record.OrderId, cancellationToken);
            if (attempt is null)
            {
                await RaiseAsync(
                    tally,
                    ReconciliationKind.GatewayPaymentUnknown,
                    $"gateway_payment_unknown:{record.PaymentId}",
                    $"The gateway holds payment {record.PaymentId} of {record.AmountPaise / 100m:0.00} for order {record.OrderId ?? "(none)"}, which is not one of ours.",
                    providerReference: record.PaymentId,
                    actualAmount: record.AmountPaise / 100m,
                    cancellationToken: cancellationToken);
                continue;
            }

            if (record.AmountPaise != Money.ToPaise(attempt.Amount))
            {
                await RaiseAsync(
                    tally,
                    ReconciliationKind.PaymentMismatch,
                    $"payment_mismatch:{attempt.Id}",
                    $"The gateway holds payment {record.PaymentId} for {record.AmountPaise / 100m:0.00}, but the order is for {attempt.Amount:0.00}.",
                    bookingId: attempt.BookingId,
                    paymentAttemptId: attempt.Id,
                    providerReference: record.PaymentId,
                    expectedAmount: attempt.Amount,
                    actualAmount: record.AmountPaise / 100m,
                    cancellationToken: cancellationToken);
                continue;
            }

            if (attempt.Status != PaymentAttemptStatus.Captured)
            {
                await ApplyGatewayPaymentAsync(tally, attempt, record.PaymentId, record.AmountPaise, cancellationToken);
                continue;
            }

            if (attempt.ProviderPaymentId != record.PaymentId)
            {
                await RaiseAsync(
                    tally,
                    ReconciliationKind.PaymentMismatch,
                    $"payment_id_mismatch:{attempt.Id}",
                    $"Order {attempt.ProviderOrderId} is recorded as paid by {attempt.ProviderPaymentId}, but the gateway shows payment {record.PaymentId}.",
                    bookingId: attempt.BookingId,
                    paymentAttemptId: attempt.Id,
                    providerReference: record.PaymentId,
                    cancellationToken: cancellationToken);
                continue;
            }

            // What the gateway has refunded against this payment should be what we have asked it to refund.
            var ours = await db.Refunds.AsNoTracking()
                .Where(r => r.PaymentAttemptId == attempt.Id && r.Status != RefundStatus.Failed)
                .Select(r => r.Amount)
                .ToListAsync(cancellationToken);
            var ourPaise = Money.ToPaise(ours.Sum());
            if (ourPaise != record.AmountRefundedPaise)
            {
                await RaiseAsync(
                    tally,
                    ReconciliationKind.RefundTotalMismatch,
                    $"refund_total_mismatch:{attempt.Id}",
                    $"For payment {record.PaymentId} the gateway has refunded {record.AmountRefundedPaise / 100m:0.00}, but our refunds add up to {ourPaise / 100m:0.00}.",
                    bookingId: attempt.BookingId,
                    paymentAttemptId: attempt.Id,
                    providerReference: record.PaymentId,
                    expectedAmount: ourPaise / 100m,
                    actualAmount: record.AmountRefundedPaise / 100m,
                    cancellationToken: cancellationToken);
            }
        }

        // Our payments against the gateway's. Only meaningful if the gateway's list was complete.
        if (listed.Value.Complete)
        {
            var seen = received.Select(p => p.PaymentId).ToHashSet(StringComparer.Ordinal);
            var recordedToday = await db.PaymentAttempts.AsNoTracking()
                .Where(a => a.Status == PaymentAttemptStatus.Captured && a.UpdatedAt >= dayStart && a.UpdatedAt < dayEnd)
                .ToListAsync(cancellationToken);
            foreach (var attempt in recordedToday.Where(a => a.ProviderPaymentId is not null && !seen.Contains(a.ProviderPaymentId)))
            {
                tally.Checked++;
                await RaiseAsync(
                    tally,
                    ReconciliationKind.PaymentMissingAtGateway,
                    $"payment_missing_at_gateway:{attempt.Id}",
                    $"Payment {attempt.ProviderPaymentId} of {attempt.Amount:0.00} is recorded as received on {day:yyyy-MM-dd}, but the gateway does not list it as captured.",
                    bookingId: attempt.BookingId,
                    paymentAttemptId: attempt.Id,
                    providerReference: attempt.ProviderPaymentId,
                    expectedAmount: attempt.Amount,
                    cancellationToken: cancellationToken);
            }
        }
        else
        {
            logger.LogWarning("The gateway had more payments for {Day} than could be fetched; the check for payments missing at the gateway was skipped", day);
        }

        // The ledger as a whole must add up to nothing, always.
        var ledgerTotal = (await db.LedgerEntries.AsNoTracking().Select(e => e.Amount).ToListAsync(cancellationToken)).Sum();
        if (ledgerTotal != 0m)
        {
            await RaiseAsync(
                tally,
                ReconciliationKind.LedgerProblem,
                $"ledger_unbalanced:{day:yyyy-MM-dd}",
                $"The ledger does not add up to zero on {day:yyyy-MM-dd}: it is off by {ledgerTotal:0.00}.",
                actualAmount: ledgerTotal,
                cancellationToken: cancellationToken);
        }

        return tally.ToSummary();
    }

    // --- Recording ---

    /// <summary>Records a problem once. Seeing it again only counts the sighting; a problem that was resolved is not re-opened.</summary>
    private async Task RaiseAsync(
        Tally tally,
        ReconciliationKind kind,
        string fingerprint,
        string detail,
        Guid? bookingId = null,
        Guid? paymentAttemptId = null,
        Guid? refundId = null,
        Guid? gatewayEventId = null,
        string? providerReference = null,
        decimal? expectedAmount = null,
        decimal? actualAmount = null,
        CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var existing = await db.ReconciliationExceptions.FirstOrDefaultAsync(e => e.Fingerprint == fingerprint, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == ReconciliationStatus.Open)
            {
                existing.SeenAgain(now);
                await db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        var created = ReconciliationException.Detect(
            kind, fingerprint, detail, now, bookingId, paymentAttemptId, refundId, gatewayEventId, providerReference, expectedAmount, actualAmount);
        db.ReconciliationExceptions.Add(created);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            tally.Raised++;
            logger.LogWarning("Payment reconciliation found a problem ({Kind}): {Detail}", kind, created.Detail);
        }
        catch (DbUpdateException)
        {
            // The same problem was recorded by another run at the same moment; that one stands.
            db.ClearTracked();
        }
    }

    private async Task RecordAutoResolvedAsync(
        Tally tally,
        ReconciliationKind kind,
        string fingerprint,
        string detail,
        string note,
        Guid? bookingId = null,
        Guid? paymentAttemptId = null,
        Guid? refundId = null,
        string? providerReference = null,
        decimal? amount = null)
    {
        if (await db.ReconciliationExceptions.AnyAsync(e => e.Fingerprint == fingerprint))
        {
            return;
        }

        var now = time.GetUtcNow();
        var created = ReconciliationException.Detect(
            kind, fingerprint, detail, now, bookingId, paymentAttemptId, refundId, null, providerReference, null, amount);
        created.AutoResolve(note, now);
        db.ReconciliationExceptions.Add(created);
        try
        {
            await db.SaveChangesAsync();
            tally.AutoResolved++;
            logger.LogWarning("Payment reconciliation fixed a difference from the gateway ({Kind}): {Detail}", kind, created.Detail);
        }
        catch (DbUpdateException)
        {
            db.ClearTracked();
        }
    }
}
