using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Payments;
using OnePondy.Domain.Bookings;

namespace OnePondy.Application.Bookings;

/// <param name="Expired">Holds released.</param>
/// <param name="Resolved">Bookings that turned out to have been paid at the gateway and were confirmed instead of expired.</param>
/// <param name="Deferred">Bookings left alone because the gateway could not be asked; they are retried on the next run.</param>
public sealed record HoldExpiryResult(int Expired, int Resolved, int Deferred);

/// <summary>
/// Releases inventory held by a booking whose hold timed out before payment or confirmation (spec §17.4). A booking
/// that was waiting for payment is first checked against the gateway, because a payment made at the last moment may
/// not have been reported to us yet (spec §17: "ask the gateway for the payment status" before expiring).
/// </summary>
public sealed class BookingHoldExpiry(
    IOnePondyDbContext db,
    BookingService bookings,
    PaymentService payments,
    TimeProvider time,
    IOptions<PaymentsOptions> options)
{
    private const int BatchSize = 100;

    public async Task<HoldExpiryResult> ExpireDueHoldsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.Bookings
            .AsNoTracking()
            .Where(b => (b.Status == BookingStatus.Pending || b.Status == BookingStatus.AwaitingPayment) && b.ExpiresAt < now)
            .OrderBy(b => b.ExpiresAt)
            .Take(BatchSize)
            .Select(b => new { b.Id, b.Status, b.ExpiresAt })
            .ToListAsync(cancellationToken);

        var expired = 0;
        var resolved = 0;
        var deferred = 0;
        foreach (var booking in due)
        {
            if (booking.Status == BookingStatus.AwaitingPayment)
            {
                var check = await payments.CheckBeforeExpiryAsync(booking.Id, cancellationToken);
                if (check == ExpiryCheck.Resolved)
                {
                    resolved++;
                    continue;
                }

                // Cannot tell whether it was paid: keep the seats for a while rather than risk taking a paying
                // customer's booking. After the grace period the hold is released anyway, and a payment that
                // still arrives is refunded in full.
                if (check == ExpiryCheck.Unknown && now - booking.ExpiresAt!.Value < options.Value.GatewayCheckGrace)
                {
                    deferred++;
                    continue;
                }
            }

            // Each booking expires in its own save, so one lost race (another worker instance got there
            // first) never blocks the rest of the batch.
            if (await bookings.TryExpireAsync(booking.Id, cancellationToken))
            {
                expired++;
            }
        }

        return new HoldExpiryResult(expired, resolved, deferred);
    }
}
