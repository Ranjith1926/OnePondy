using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OnePondy.Application.Common;
using OnePondy.Domain.Rides;

namespace OnePondy.Application.Rides;

/// <summary>
/// Gives up on ride requests no driver took in time, and clears old fare quotes. A rider is not left searching for ever: when the search window
/// ends the ride expires and the rider is told, so they can try again or choose something else.
/// </summary>
public sealed class RideExpiry(IOnePondyDbContext db, IRideNotifier notifier, TimeProvider time, ILogger<RideExpiry> logger)
{
    private const int BatchSize = 100;

    /// <summary>How long a quote is kept after it stops being valid; there is nothing in one worth keeping once the day is over.</summary>
    public static readonly TimeSpan QuoteKeptFor = TimeSpan.FromDays(1);

    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.Rides
            .Where(r => r.Status == RideStatus.Requested && r.SearchEndsAt <= now)
            .OrderBy(r => r.SearchEndsAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var ride in due)
        {
            ride.Expire(now);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (WriteConflicts.Is(ex))
            {
                // A driver accepted, or the rider cancelled, at the same moment: that is the right outcome, so this ride is left alone.
                db.ClearTracked();
                continue;
            }

            expired++;
            try
            {
                await notifier.RideChangedAsync(ride.Id, ride.RiderUserId, null, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not push the expiry of ride {RideId}", ride.Id);
            }
        }

        return expired;
    }

    /// <summary>Removes quotes that expired long ago and were never used for a ride.</summary>
    public async Task<int> DeleteOldQuotesAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - QuoteKeptFor;
        var old = await db.RideQuotes
            .Where(q => q.ExpiresAt < cutoff && !db.Rides.Any(r => r.QuoteId == q.Id))
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (old.Count == 0)
        {
            return 0;
        }

        db.RideQuotes.RemoveRange(old);
        await db.SaveChangesAsync(cancellationToken);
        return old.Count;
    }
}
