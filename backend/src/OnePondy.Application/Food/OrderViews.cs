using OnePondy.Application.Bookings;
using OnePondy.Application.Common;
using OnePondy.Application.Delivery;
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

    /// <summary>
    /// Whether food can only be ordered while online payment is available (the `payments` module is on). On by default: with payment off an
    /// accepted order would be confirmed with nothing paid, and cash on delivery (step 8.8) is not built to cover that. Turn it off only to
    /// try the unpaid path in development.
    /// </summary>
    public bool RequireOnlinePayment { get; set; } = true;

    /// <summary>How long an order may sit in each stage before the administrators' list flags it as needing attention (step 8.9).</summary>
    public AttentionOptions Attention { get; set; } = new();

    /// <summary>Limits on cash on delivery (step 8.8). Whether it is offered at all is decided by the `cashOnDelivery` module and each restaurant's opt-in.</summary>
    public CashOnDeliveryOptions CashOnDelivery { get; set; } = new();
}

public sealed class CashOnDeliveryOptions
{
    /// <summary>The largest order that can be paid in cash. A larger one must be paid online.</summary>
    public decimal MaxOrderAmount { get; set; } = 2000m;

    /// <summary>
    /// The most cash one driver may hold before handing it in. A driver at the limit is not given another cash order until they do, so what
    /// OnePondy trusts any one person with stays bounded.
    /// </summary>
    public decimal MaxDriverCashHeld { get; set; } = 5000m;
}

public sealed class AttentionOptions
{
    /// <summary>An order ready for pickup that no driver has taken.</summary>
    public TimeSpan NoDriverAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>A driver who took a delivery but has not collected it.</summary>
    public TimeSpan PickupOverdueAfter { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>A delivery that has been out longer than this.</summary>
    public TimeSpan DeliveryOverdueAfter { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>A kitchen that has not started on a confirmed order, or has run past its estimate.</summary>
    public TimeSpan KitchenOverdueAfter { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>A refund still pending this long after the order was closed.</summary>
    public TimeSpan RefundOverdueAfter { get; set; } = TimeSpan.FromHours(1);
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

        var cash = options.CashOnDelivery;
        if (cash.MaxOrderAmount is < 100m or > 100_000m || cash.MaxDriverCashHeld < cash.MaxOrderAmount || cash.MaxDriverCashHeld > 1_000_000m)
        {
            throw new InvalidOperationException("Food:CashOnDelivery:MaxOrderAmount must be 100 to 100000, and MaxDriverCashHeld at least that and at most 1000000.");
        }

        var attention = options.Attention;
        TimeSpan[] limits = [attention.NoDriverAfter, attention.PickupOverdueAfter, attention.DeliveryOverdueAfter, attention.KitchenOverdueAfter, attention.RefundOverdueAfter];
        if (limits.Any(l => l < TimeSpan.FromMinutes(1) || l > TimeSpan.FromDays(1)))
        {
            throw new InvalidOperationException("Each Food:Attention limit must be between 1 minute and 1 day.");
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
    decimal? ExpectedTotal,
    /// <summary>"online" (the default) or "cashOnDelivery". Cash is only accepted where the restaurant and OnePondy offer it.</summary>
    string? PaymentMethod = null);

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
    OrderPriceView Price,
    PaymentOptionsView PaymentOptions);

/// <summary>How this cart can be paid for right now. Both can be true; if neither is, it cannot be ordered at all.</summary>
public sealed record PaymentOptionsView(bool Online, bool CashOnDelivery, decimal CashOnDeliveryMaxAmount);

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
    IReadOnlyList<RefundView> Refunds,
    /// <summary>How the delivery is going once the order is ready; null before that and after a cancelled delivery.</summary>
    DeliveryProgressView? Delivery,
    PaymentMethod PaymentMethod,
    /// <summary>For cash on delivery: the cash the driver confirmed taking, once delivered.</summary>
    decimal? CashCollected);

public sealed record OrderSummary(
    Guid Id,
    Guid BusinessId,
    string RestaurantName,
    FoodOrderStatus Status,
    DateTimeOffset? ExpiresAt,
    int ItemCount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    PaymentMethod PaymentMethod);

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
    DateTimeOffset UpdatedAt,
    /// <summary>Who is collecting the order and how far along it is; null before the order is ready.</summary>
    DeliveryProgressView? Delivery,
    PaymentMethod PaymentMethod);

/// <summary>
/// One row of the restaurant's inbox. It carries the dishes (the kitchen needs them to cook) and the order's <c>Version</c>, which
/// every action must send back so a stale screen cannot act on an order that has since changed. Like the detail, it leaves out the
/// customer's address and phone.
/// </summary>
public sealed record PartnerOrderSummary(
    Guid Id,
    uint Version,
    FoodOrderStatus Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? EstimatedReadyAt,
    int ItemCount,
    IReadOnlyList<OrderLineView> Lines,
    decimal TotalAmount,
    string Currency,
    string DeliveryArea,
    DateTimeOffset CreatedAt,
    DeliveryProgressView? Delivery,
    PaymentMethod PaymentMethod);

public static class OrderErrors
{
    public static readonly AppError NotFound = new("order_not_found", ErrorKind.NotFound, "That order does not exist.");

    public static readonly AppError ModuleOff = new(
        "food_not_available",
        ErrorKind.Conflict,
        "Food ordering is not available yet.");

    public static readonly AppError PaymentUnavailable = new(
        "food_payment_unavailable",
        ErrorKind.Conflict,
        "Food can only be ordered while online payment is available, and it isn't right now. Please try again later.");

    public static readonly AppError CashUnavailable = new(
        "cash_on_delivery_unavailable",
        ErrorKind.Conflict,
        "Cash on delivery isn't available for this order. Pay online instead.");

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
