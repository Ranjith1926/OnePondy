using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Domain.Bookings;

namespace OnePondy.Application.Bookings;

/// <summary>Releases inventory held by a booking whose hold timed out before payment or confirmation (spec §17.4).</summary>
public sealed class BookingHoldExpiry(IOnePondyDbContext db, BookingService bookings, TimeProvider time)
{
    private const int BatchSize = 100;

    public async Task<int> ExpireDueHoldsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var dueIds = await db.Bookings
            .AsNoTracking()
            .Where(b => (b.Status == BookingStatus.Pending || b.Status == BookingStatus.AwaitingPayment) && b.ExpiresAt < now)
            .OrderBy(b => b.ExpiresAt)
            .Take(BatchSize)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var id in dueIds)
        {
            // Each booking expires in its own save, so one lost race (another worker instance got there
            // first) never blocks the rest of the batch.
            if (await bookings.TryExpireAsync(id, cancellationToken))
            {
                expired++;
            }
        }

        return expired;
    }
}
