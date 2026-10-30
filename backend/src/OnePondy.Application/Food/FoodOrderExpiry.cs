using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Bookings;
using OnePondy.Application.Common;
using OnePondy.Application.Payments;
using OnePondy.Domain.Food;

namespace OnePondy.Application.Food;

/// <summary>
/// Closes food orders the restaurant did not answer, or the customer did not pay for, in time (step 8.3). An order that was waiting for
/// payment is first checked against the gateway, as a booking is: a payment made at the last moment may not have been reported to us
/// yet, and that customer should get their food, not a refund.
/// </summary>
public sealed class FoodOrderExpiry(
    IOnePondyDbContext db,
    OrderService orders,
    PaymentService payments,
    TimeProvider time,
    IOptions<FoodOptions> options)
{
    private const int BatchSize = 100;

    public async Task<HoldExpiryResult> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.FoodOrders.AsNoTracking()
            .Where(o => (o.Status == FoodOrderStatus.Placed || o.Status == FoodOrderStatus.AwaitingPayment) && o.ExpiresAt < now)
            .OrderBy(o => o.ExpiresAt)
            .Take(BatchSize)
            .Select(o => new { o.Id, o.Status, o.ExpiresAt })
            .ToListAsync(cancellationToken);

        var expired = 0;
        var resolved = 0;
        var deferred = 0;
        foreach (var order in due)
        {
            // Asked before the order is loaded to be changed: applying a payment found here works on its own fresh copy, and the
            // expiry below then loads whatever that left.
            if (order.Status == FoodOrderStatus.AwaitingPayment)
            {
                var check = await payments.CheckFoodOrderBeforeExpiryAsync(order.Id, cancellationToken);
                if (check == ExpiryCheck.Resolved)
                {
                    resolved++;
                    continue;
                }

                // Cannot tell whether it was paid: wait a little rather than risk cancelling a paying customer's food. After the grace
                // period it expires anyway, and a payment that still arrives is refunded in full.
                if (check == ExpiryCheck.Unknown && now - order.ExpiresAt!.Value < options.Value.GatewayCheckGrace)
                {
                    deferred++;
                    continue;
                }
            }

            // Each order expires in its own save, so a lost race on one (accepted, paid or cancelled at that moment) never blocks the rest.
            if (await orders.TryExpireAsync(order.Id, cancellationToken))
            {
                expired++;
            }
        }

        return new HoldExpiryResult(expired, resolved, deferred);
    }
}
