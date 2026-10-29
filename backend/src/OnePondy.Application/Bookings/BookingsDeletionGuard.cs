using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Bookings;

namespace OnePondy.Application.Bookings;

/// <summary>
/// Refuses account deletion while a booking is still in progress, for the customer who made it and
/// for the partner who owns the business it is with (spec §7, Account deletion workflow). Orders,
/// rides and refunds add their own checks in later phases.
/// </summary>
public sealed class BookingsDeletionGuard(IOnePondyDbContext db) : IAccountDeletionGuard
{
    public const string CustomerBookingsInProgress = "bookings_in_progress";
    public const string PartnerBookingsInProgress = "partner_bookings_in_progress";

    private static readonly BookingStatus[] InProgress =
    [
        BookingStatus.Pending,
        BookingStatus.AwaitingPayment,
        BookingStatus.Confirmed,
        BookingStatus.RefundPending,
    ];

    public async Task<IReadOnlyList<string>> GetBlockersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var blockers = new List<string>();

        if (await db.Bookings.AsNoTracking().AnyAsync(b => b.CustomerUserId == userId && InProgress.Contains(b.Status), cancellationToken))
        {
            blockers.Add(CustomerBookingsInProgress);
        }

        var ownedBusinessIds = await db.Businesses.AsNoTracking()
            .Where(b => b.OwnerUserId == userId)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);
        if (ownedBusinessIds.Count > 0
            && await db.Bookings.AsNoTracking().AnyAsync(b => ownedBusinessIds.Contains(b.BusinessId) && InProgress.Contains(b.Status), cancellationToken))
        {
            blockers.Add(PartnerBookingsInProgress);
        }

        return blockers;
    }
}
