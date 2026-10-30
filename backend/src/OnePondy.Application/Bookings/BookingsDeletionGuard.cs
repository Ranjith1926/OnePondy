using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Food;

namespace OnePondy.Application.Bookings;

/// <summary>
/// Refuses account deletion while a booking or a food order is still in progress (including one waiting for its refund), for the
/// customer who made it and for the partner who owns the business it is with (spec §7, Account deletion workflow). It is the one
/// deletion guard registered, so food orders are checked here rather than by a second guard. Rides add their own checks later.
/// </summary>
public sealed class BookingsDeletionGuard(IOnePondyDbContext db) : IAccountDeletionGuard
{
    public const string CustomerBookingsInProgress = "bookings_in_progress";
    public const string PartnerBookingsInProgress = "partner_bookings_in_progress";
    public const string CustomerFoodOrdersInProgress = "food_orders_in_progress";
    public const string PartnerFoodOrdersInProgress = "partner_food_orders_in_progress";

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

        return blockers;
    }
}
