using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Payments;

/// <summary>
/// The only writer of <c>ledger_entries</c> (spec Phase 7: "ledger entries balance for every payment, refund and
/// settlement"). Every business event is one transaction whose lines add up to zero, and the database refuses to commit
/// one that does not. Debits are positive and credits negative.
/// <list type="bullet">
/// <item>Payment that confirms a booking: <c>gateway_clearing</c> debit; <c>platform_commission</c> and the partner's
/// <c>partner_payable</c> credit.</item>
/// <item>Payment that no booking wants (late, duplicate): <c>gateway_clearing</c> debit; <c>customer_refunds</c> credit,
/// the money held for the customer.</item>
/// <item>Refund processed: the reverse of whichever of those applied, in proportion.</item>
/// </list>
/// Lines are added to the caller's unit of work, so a ledger entry and the booking change it describes are saved together.
/// A transaction id is derived from the event (for example <c>capture:{attempt}</c>), so posting the same event twice is
/// recognised and does nothing.
/// </summary>
public sealed class LedgerService(IOnePondyDbContext db, TimeProvider time, IOptions<PaymentsOptions> options)
{
    /// <summary>A stable id for a business event, so a repeat of the event finds its earlier entries.</summary>
    public static Guid TransactionId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));

    public static string CaptureKey(Guid paymentAttemptId) => $"capture:{paymentAttemptId}";

    public static string RefundKey(Guid refundId) => $"refund:{refundId}";

    /// <summary>The platform's share of a payment, to the paisa. Provisional rate until the commission is agreed (spec D21).</summary>
    public static decimal CommissionFor(decimal amount, decimal percent) =>
        Math.Round(amount * percent / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>A payment that confirms a booking: the partner is owed their share and the platform keeps its commission.</summary>
    public async Task PostCaptureAsync(Booking booking, PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var transaction = TransactionId(CaptureKey(attempt.Id));
        if (await AlreadyPostedAsync(transaction, cancellationToken))
        {
            return;
        }

        var commission = CommissionFor(attempt.Amount, options.Value.CommissionPercent);
        var partner = attempt.Amount - commission;
        var now = time.GetUtcNow();
        Add(transaction, LedgerAccounts.GatewayClearing, attempt.Amount, booking.Id, "Payment received", now);
        Add(transaction, LedgerAccounts.PlatformCommission, -commission, booking.Id, "Commission", now);
        Add(transaction, LedgerAccounts.PartnerPayable(booking.BusinessId), -partner, booking.Id, "Partner share", now);
    }

    /// <summary>A payment no booking wants: held for the customer until it is refunded.</summary>
    public async Task PostHeldPaymentAsync(Booking booking, PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var transaction = TransactionId(CaptureKey(attempt.Id));
        if (await AlreadyPostedAsync(transaction, cancellationToken))
        {
            return;
        }

        var now = time.GetUtcNow();
        Add(transaction, LedgerAccounts.GatewayClearing, attempt.Amount, booking.Id, "Payment received, to be refunded", now);
        Add(transaction, LedgerAccounts.CustomerRefunds, -attempt.Amount, booking.Id, "Held for refund", now);
    }

    /// <summary>The money has gone back to the customer: undo the payment's entries for the amount refunded.</summary>
    public async Task PostRefundAsync(Refund refund, Booking booking, PaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var transaction = TransactionId(RefundKey(refund.Id));
        if (await AlreadyPostedAsync(transaction, cancellationToken))
        {
            return;
        }

        var now = time.GetUtcNow();
        if (refund.Reason == RefundReason.LatePayment)
        {
            Add(transaction, LedgerAccounts.CustomerRefunds, refund.Amount, booking.Id, "Refund paid", now);
            Add(transaction, LedgerAccounts.GatewayClearing, -refund.Amount, booking.Id, "Refund paid out", now);
            return;
        }

        // The platform gives back its commission in proportion to what was refunded, using the commission actually
        // booked when the payment arrived, not today's rate.
        var captureTransaction = TransactionId(CaptureKey(attempt.Id));
        var commissionKept = -await db.LedgerEntries
            .Where(e => e.TransactionId == captureTransaction && e.Account == LedgerAccounts.PlatformCommission)
            .SumAsync(e => e.Amount, cancellationToken);
        var commissionBack = refund.Amount == attempt.Amount
            ? commissionKept
            : Math.Round(commissionKept * refund.Amount / attempt.Amount, 2, MidpointRounding.AwayFromZero);
        var partnerBack = refund.Amount - commissionBack;

        Add(transaction, LedgerAccounts.GatewayClearing, -refund.Amount, booking.Id, "Refund paid out", now);
        Add(transaction, LedgerAccounts.PlatformCommission, commissionBack, booking.Id, "Commission returned", now);
        Add(transaction, LedgerAccounts.PartnerPayable(booking.BusinessId), partnerBack, booking.Id, "Partner share returned", now);
    }

    public static string PayoutKey(Guid settlementId) => $"payout:{settlementId}";

    /// <summary>
    /// A statement has been paid to the partner: what OnePondy owes them goes down and what it has paid them goes up. Not tied to
    /// a booking, since it settles a whole statement. The transaction id comes from the statement, so a payout cannot be posted
    /// twice (the database refuses a second one).
    /// </summary>
    public void PostPayout(Settlement settlement)
    {
        if (settlement.NetAmount <= 0m)
        {
            return;
        }

        var transaction = TransactionId(PayoutKey(settlement.Id));
        var now = time.GetUtcNow();
        Add(transaction, LedgerAccounts.PartnerPayable(settlement.BusinessId), settlement.NetAmount, null, "Statement paid out", now);
        Add(transaction, LedgerAccounts.PartnerPaid(settlement.BusinessId), -settlement.NetAmount, null, "Paid to partner", now);
    }

    private Task<bool> AlreadyPostedAsync(Guid transaction, CancellationToken cancellationToken) =>
        db.LedgerEntries.AsNoTracking().AnyAsync(e => e.TransactionId == transaction, cancellationToken);

    /// <summary>Adds a line unless it is zero (a 0% commission, say), which a balanced transaction does not need.</summary>
    private void Add(Guid transaction, string account, decimal amount, Guid? bookingId, string description, DateTimeOffset now)
    {
        if (amount != 0m)
        {
            db.LedgerEntries.Add(LedgerEntry.Create(transaction, account, amount, bookingId, description, now));
        }
    }
}
