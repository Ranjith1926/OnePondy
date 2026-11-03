using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Food;
using OnePondy.Domain.Payments;
using OnePondy.Domain.Rides;

namespace OnePondy.Application.Bookings;

/// <summary>
/// Refuses account deletion while a booking or a food order is still in progress (including one waiting for its refund), for the
/// customer who made it and for the partner who owns the business it is with (spec §7, Account deletion workflow). It is the one
/// deletion guard registered, so food orders and rides are checked here rather than by a second guard.
/// </summary>
public sealed class BookingsDeletionGuard(IOnePondyDbContext db) : IAccountDeletionGuard
{
    public const string CustomerBookingsInProgress = "bookings_in_progress";
    public const string PartnerBookingsInProgress = "partner_bookings_in_progress";
    public const string CustomerFoodOrdersInProgress = "food_orders_in_progress";
    public const string PartnerFoodOrdersInProgress = "partner_food_orders_in_progress";
    public const string RideInProgress = "ride_in_progress";
    public const string DriverOnDuty = "driver_on_duty";
    public const string DriverHoldsCash = "driver_holds_cash";

    private static readonly BookingStatus[] InProgress =
    [
        BookingStatus.Pending,
        BookingStatus.AwaitingPayment,
        BookingStatus.Confirmed,
        BookingStatus.RefundPending,
    ];

    private static readonly FoodOrderStatus[] FoodInProgress =
    [
        FoodOrderStatus.Placed,
        FoodOrderStatus.AwaitingPayment,
        FoodOrderStatus.Confirmed,
        FoodOrderStatus.Preparing,
        FoodOrderStatus.ReadyForPickup,
        FoodOrderStatus.OutForDelivery,
        FoodOrderStatus.RefundPending,
    ];

    public async Task<IReadOnlyList<string>> GetBlockersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var blockers = new List<string>();

        if (await db.Bookings.AsNoTracking().AnyAsync(b => b.CustomerUserId == userId && InProgress.Contains(b.Status), cancellationToken))
        {
            blockers.Add(CustomerBookingsInProgress);
        }

        if (await db.FoodOrders.AsNoTracking().AnyAsync(o => o.CustomerUserId == userId && FoodInProgress.Contains(o.Status), cancellationToken))
        {
            blockers.Add(CustomerFoodOrdersInProgress);
        }

        // A rider with a ride that has not ended, or a driver holding one, finishes it first.
        if (await db.Rides.AsNoTracking().AnyAsync(
                r => (r.RiderUserId == userId || r.DriverUserId == userId)
                    && (r.Status == RideStatus.Requested || r.Status == RideStatus.Accepted || r.Status == RideStatus.Arrived || r.Status == RideStatus.InProgress),
                cancellationToken))
        {
            blockers.Add(RideInProgress);
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

        if (ownedBusinessIds.Count > 0
            && await db.FoodOrders.AsNoTracking().AnyAsync(o => ownedBusinessIds.Contains(o.BusinessId) && FoodInProgress.Contains(o.Status), cancellationToken))
        {
            blockers.Add(PartnerFoodOrdersInProgress);
        }

        // A driver on duty may be offered a job at any moment, so they go off duty first (jobs in progress add their own check in step 8.7).
        if (await db.DriverProfiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.IsOnDuty, cancellationToken)
            || await db.RideDriverProfiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.IsOnDuty, cancellationToken))
        {
            blockers.Add(DriverOnDuty);
        }

        // Cash a driver collected for OnePondy is theirs to hand in before their account goes (step 8.8).
        var held = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.Account == LedgerAccounts.DriverCash(userId))
            .SumAsync(e => e.Amount, cancellationToken);
        if (held > 0m)
        {
            blockers.Add(DriverHoldsCash);
        }

        return blockers;
    }
}
