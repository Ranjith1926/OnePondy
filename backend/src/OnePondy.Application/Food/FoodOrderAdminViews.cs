using OnePondy.Application.Common;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Food;

namespace OnePondy.Application.Food;

/// <summary>Why an order needs an administrator's eye (step 8.9). Each is "stuck for longer than expected", never a failure in itself.</summary>
public enum OrderAttention
{
    /// <summary>Ready for pickup, and no driver has taken it.</summary>
    NoDriver = 1,

    /// <summary>A driver took it but has not collected it.</summary>
    PickupOverdue = 2,

    /// <summary>Out for delivery for longer than expected.</summary>
    DeliveryOverdue = 3,

    /// <summary>Confirmed but not started, or cooking past its estimate.</summary>
    KitchenOverdue = 4,

    /// <summary>A refund still pending long after the order closed.</summary>
    RefundOverdue = 5,
}

public sealed record AdminOrderListQuery(FoodOrderStatus? Status, bool? NeedsAttention, int? Page, int? PageSize);

/// <summary>One row of the administrators' order list.</summary>
public sealed record AdminOrderSummary(
    Guid Id,
    uint Version,
    FoodOrderStatus Status,
    string RestaurantName,
    string DeliveryArea,
    int ItemCount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DeliveryJobStatus? DeliveryStatus,
    string? DriverFirstName,
    OrderAttention? Attention);

/// <summary>Where the driver last reported being. Shown to administrators only, and only while the delivery is under way.</summary>
public sealed record AdminDriverPosition(double Latitude, double Longitude, DateTimeOffset At);

/// <summary>The delivery of an order as an administrator sees it, including who has it and how to reach them.</summary>
public sealed record AdminDeliveryView(
    Guid JobId,
    uint JobVersion,
    DeliveryJobStatus Status,
    Guid? DriverUserId,
    string? DriverName,
    string? DriverPhone,
    VehicleType? VehicleType,
    DateTimeOffset OpenedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    AdminDriverPosition? Position,
    /// <summary>True while a driver has the delivery but has not collected the food: it can be offered to the other drivers again.</summary>
    bool CanRelease);

/// <summary>An order with everything support needs: what the customer sees, the delivery, and what an administrator may do.</summary>
public sealed record AdminOrderDetail(
    OrderDetail Order,
    AdminDeliveryView? Delivery,
    OrderAttention? Attention,
    /// <summary>False once the order has arrived or closed.</summary>
    bool CanCancel);

public static class AdminOrderErrors
{
    public static readonly AppError JobChanged = new(
        "delivery_changed",
        ErrorKind.Conflict,
        "This delivery changed since you loaded it. Reload and try again.");

    public static readonly AppError NothingToRelease = new(
        "delivery_not_releasable",
        ErrorKind.Conflict,
        "Only a delivery that a driver has taken but not collected can be offered to the other drivers again.");
}

/// <summary>The rules for "needs attention", in one place so they can be tested row by row.</summary>
public static class OrderAttentionRules
{
    /// <summary>The order statuses that can need attention: everything still in progress.</summary>
    public static readonly FoodOrderStatus[] Watched =
    [
        FoodOrderStatus.Confirmed,
        FoodOrderStatus.Preparing,
        FoodOrderStatus.ReadyForPickup,
        FoodOrderStatus.OutForDelivery,
        FoodOrderStatus.RefundPending,
    ];

    public static OrderAttention? For(FoodOrder order, DeliveryJob? job, AttentionOptions limits, DateTimeOffset now)
    {
        switch (order.Status)
        {
            case FoodOrderStatus.Confirmed when now - order.UpdatedAt > limits.KitchenOverdueAfter:
                return OrderAttention.KitchenOverdue;

            case FoodOrderStatus.Preparing when order.EstimatedReadyAt is { } ready && now - ready > limits.KitchenOverdueAfter:
                return OrderAttention.KitchenOverdue;

            case FoodOrderStatus.ReadyForPickup when job is { Status: DeliveryJobStatus.Open } && now - job.OpenedAt > limits.NoDriverAfter:
                return OrderAttention.NoDriver;

            case FoodOrderStatus.ReadyForPickup when job is { Status: DeliveryJobStatus.Accepted, AcceptedAt: { } accepted } && now - accepted > limits.PickupOverdueAfter:
                return OrderAttention.PickupOverdue;

            case FoodOrderStatus.OutForDelivery when job is { Status: DeliveryJobStatus.PickedUp, PickedUpAt: { } picked } && now - picked > limits.DeliveryOverdueAfter:
                return OrderAttention.DeliveryOverdue;

            case FoodOrderStatus.RefundPending when now - order.UpdatedAt > limits.RefundOverdueAfter:
                return OrderAttention.RefundOverdue;

            default:
                return null;
        }
    }
}
