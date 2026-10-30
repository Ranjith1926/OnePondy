using OnePondy.Application.Bookings;
using OnePondy.Application.Common;
using OnePondy.Domain.Food;

namespace OnePondy.Application.Food;

/// <summary>Settings for food ordering (spec Phase 8, decisions F4 and F6). Fees are flat and shown before the customer pays.</summary>
public sealed class FoodOptions
{
    public const string SectionName = "Food";

    /// <summary>How long a restaurant has to accept or reject a new order.</summary>
    public TimeSpan AcceptWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a customer has to pay once the restaurant accepts.</summary>
    public TimeSpan PaymentWindow { get; set; } = TimeSpan.FromMinutes(10);

    public decimal DeliveryFee { get; set; } = 30m;

    public decimal PackagingFee { get; set; } = 10m;

    /// <summary>
    /// How long past its payment window an unpaid order may wait when the gateway cannot be asked whether it was paid (step 8.3). Shorter
    /// than the bookings' grace, because a restaurant is waiting on it; a payment that lands after this is refunded in full.
    /// </summary>
    public TimeSpan GatewayCheckGrace { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Refuses to start with food settings that would give nonsense prices or windows.</summary>
public static class FoodOptionsGuard
{
    public static void EnsureSafe(FoodOptions options)
    {
        if (options.AcceptWindow < TimeSpan.FromMinutes(1) || options.AcceptWindow > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException("Food:AcceptWindow must be between 1 and 30 minutes.");
        }

        if (options.PaymentWindow < TimeSpan.FromMinutes(5) || options.PaymentWindow > TimeSpan.FromHours(2))
        {
            throw new InvalidOperationException("Food:PaymentWindow must be between 5 minutes and 2 hours.");
        }

        if (options.DeliveryFee is < 0m or > 500m || options.PackagingFee is < 0m or > 500m)
        {
            throw new InvalidOperationException("Food:DeliveryFee and Food:PackagingFee must each be between 0 and 500.");
        }

        if (options.GatewayCheckGrace < TimeSpan.FromMinutes(1) || options.GatewayCheckGrace > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("Food:GatewayCheckGrace must be between 1 minute and 1 hour.");
        }
    }
}

/// <summary>One dish a customer wants, as the app sends it. The price is never sent: the server reads it from the menu.</summary>
public sealed record OrderLineInput(Guid MenuItemId, int Quantity);

/// <summary>Everything needed to price a cart.</summary>
public sealed record QuoteInput(Guid BusinessId, string? Area, IReadOnlyList<OrderLineInput> Lines);

/// <summary>
/// A customer places an order. <c>ExpectedTotal</c> is the total they were shown: if the menu has changed since, the order is refused
/// and they are shown the new price instead of being charged a different one.
/// </summary>
public sealed record PlaceOrderInput(
    string IdempotencyKey,
    Guid BusinessId,
    string? Area,
    string? Address,
    string? Note,
    string? ContactPhone,
    IReadOnlyList<OrderLineInput> Lines,
    decimal? ExpectedTotal);

public sealed record OrderLineView(
    Guid MenuItemId,
    string Name,
    bool IsVeg,
    decimal UnitPrice,
    int Quantity,
    decimal TaxRatePercent,
    decimal Subtotal,
    decimal TaxAmount);

public sealed record OrderPriceView(
    decimal Subtotal,
    decimal DeliveryFee,
    decimal PackagingFee,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string Currency);

public sealed record OrderQuoteView(
    Guid BusinessId,
    string RestaurantName,
    string Area,
    int PrepTimeMinutes,
    decimal MinOrderAmount,
    IReadOnlyList<OrderLineView> Lines,
    OrderPriceView Price);

public sealed record OrderHistoryView(
    FoodOrderStatus? PreviousStatus,
    FoodOrderStatus Status,
    FoodOrderActorType ActorType,
    string? Reason,
    DateTimeOffset At);

/// <summary>An order as the customer who placed it sees it, including where it goes.</summary>
public sealed record OrderDetail(
    Guid Id,
    uint Version,
    Guid BusinessId,
    string RestaurantName,
    string? FssaiLicenseNumber,
    FoodOrderStatus Status,
    bool CanCancel,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? EstimatedReadyAt,
    int PrepTimeMinutes,
    string DeliveryArea,
    string DeliveryAddress,
    string? DeliveryNote,
    string ContactPhone,
    IReadOnlyList<OrderLineView> Lines,
    OrderPriceView Price,
    string? ClosedReason,
    IReadOnlyList<OrderHistoryView> History,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>The payment that confirmed the order, as a receipt; null while unpaid or when nothing was paid online.</summary>
    PaymentView? Payment,
    IReadOnlyList<RefundView> Refunds);

public sealed record OrderSummary(
    Guid Id,
    Guid BusinessId,
    string RestaurantName,
    FoodOrderStatus Status,
    DateTimeOffset? ExpiresAt,
    int ItemCount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt);

/// <summary>
/// An order as the restaurant sees it. The customer's address and phone are left out on purpose: in v1 OnePondy's own drivers
/// deliver (decision F1), so the restaurant only needs the area and the dishes.
/// </summary>
public sealed record PartnerOrderDetail(
    Guid Id,
    uint Version,
    FoodOrderStatus Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? EstimatedReadyAt,
    int PrepTimeMinutes,
    string DeliveryArea,
    IReadOnlyList<OrderLineView> Lines,
    OrderPriceView Price,
    string? ClosedReason,
    IReadOnlyList<OrderHistoryView> History,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PartnerOrderSummary(
    Guid Id,
    FoodOrderStatus Status,
    DateTimeOffset? ExpiresAt,
    int ItemCount,
    decimal TotalAmount,
    string Currency,
    string DeliveryArea,
    DateTimeOffset CreatedAt);

public static class OrderErrors
{
    public static readonly AppError NotFound = new("order_not_found", ErrorKind.NotFound, "That order does not exist.");

    public static readonly AppError ModuleOff = new(
        "food_not_available",
        ErrorKind.Conflict,
        "Food ordering is not available yet.");

    public static readonly AppError RestaurantClosed = new(
        "restaurant_not_taking_orders",
        ErrorKind.Conflict,
        "This restaurant is not taking orders right now.");

    public static readonly AppError AreaNotServed = new(
        "restaurant_area_not_served",
        ErrorKind.Validation,
        "This restaurant does not deliver to that area.");

    public static readonly AppError Changed = new(
        "order_changed",
        ErrorKind.Conflict,
        "This order changed since you loaded it. Reload and try again.");

    public static AppError ItemUnavailable(IEnumerable<string> names) => new(
        "order_item_unavailable",
        ErrorKind.Conflict,
        "These dishes are not available any more: " + string.Join(", ", names) + ". Remove them and try again.");

    public static AppError BelowMinimum(decimal minimum) => new(
        "order_below_minimum",
        ErrorKind.Validation,
        $"This restaurant's smallest order is ₹{minimum:0.##}.");

    public static AppError PriceChanged(OrderQuoteView current) => new(
        "order_price_changed",
        ErrorKind.Conflict,
        $"The price changed. The total is now ₹{current.Price.TotalAmount:0.00}. Check your cart and place the order again.");

    public static AppError InvalidTransition(string message) => new("order_invalid_transition", ErrorKind.Conflict, message);
}
