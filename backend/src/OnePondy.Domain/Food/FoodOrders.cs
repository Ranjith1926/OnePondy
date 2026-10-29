namespace OnePondy.Domain.Food;

/// <summary>
/// Where a food order is (spec Phase 8, step 8.2). The restaurant accepts first, then the customer pays (decision F6), then the
/// kitchen and the delivery take over. <see cref="OutForDelivery"/> and <see cref="Delivered"/> are moved by the delivery side (step 8.7).
/// </summary>
public enum FoodOrderStatus
{
    Placed = 1,
    AwaitingPayment = 2,
    Confirmed = 3,
    Preparing = 4,
    ReadyForPickup = 5,
    OutForDelivery = 6,
    Delivered = 7,
    Rejected = 8,
    Cancelled = 9,
    Expired = 10,
    Failed = 11,
    RefundPending = 12,
    Refunded = 13,
}

/// <summary>Who caused an order status change.</summary>
public enum FoodOrderActorType
{
    Customer = 1,
    Partner = 2,
    Admin = 3,
    System = 4,
    Driver = 5,
}

/// <summary>One dish on an order, copied from the menu when the order is placed so later menu edits never change it.</summary>
public sealed record PricedLine(
    Guid MenuItemId,
    string Name,
    bool IsVeg,
    decimal UnitPrice,
    int Quantity,
    decimal TaxRatePercent,
    decimal Subtotal,
    decimal TaxAmount);

/// <summary>What an order costs, broken down the way the customer sees it before paying.</summary>
public sealed record OrderPrice(
    IReadOnlyList<PricedLine> Lines,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal PackagingFee,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string Currency);

/// <summary>
/// The one place food prices are worked out. Tax is worked out per line and kept apart from the price (decision F3), so changing a
/// rate later never rewrites a past order. Fees carry no tax in v1: that is part of the tax review, not guessed here.
/// </summary>
public static class FoodPricing
{
    public const string Currency = "INR";

    public static PricedLine Line(MenuItem item, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);

        var subtotal = item.Price * quantity;
        var tax = decimal.Round(subtotal * item.TaxRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return new PricedLine(item.Id, item.Name, item.IsVeg, item.Price, quantity, item.TaxRatePercent, subtotal, tax);
    }

    public static OrderPrice Total(IReadOnlyList<PricedLine> lines, decimal deliveryFee, decimal packagingFee)
    {
        var subtotal = lines.Sum(l => l.Subtotal);
        var tax = lines.Sum(l => l.TaxAmount);
        return new OrderPrice(lines, subtotal, deliveryFee, packagingFee, tax, 0m, subtotal + deliveryFee + packagingFee + tax, Currency);
    }
}

/// <summary>Where and to whom an order is delivered.</summary>
public sealed record DeliveryDetails(string Area, string Address, string? Note, string ContactPhone);

/// <summary>
/// A customer's food order. Every status change goes through one method that checks the table below, so the server (not any client)
/// decides what can happen next:
/// <code>
/// placed          → awaiting_payment | confirmed (restaurant accepts) · rejected · cancelled (customer) · expired
/// awaiting_payment→ confirmed (paid) · rejected · cancelled (customer) · expired · failed
/// confirmed       → preparing · cancelled (customer until preparing starts; restaurant or admin any time)
/// preparing       → ready_for_pickup · cancelled (restaurant or admin only)
/// ready_for_pickup→ out_for_delivery · cancelled (restaurant or admin only)
/// out_for_delivery→ delivered · cancelled (admin only)
/// rejected | cancelled → refund_pending → refunded
/// </code>
/// </summary>
public sealed class FoodOrder
{
    public const int MaxReasonLength = 500;
    public const int MaxKeyLength = 64;

    private readonly List<FoodOrderLine> _lines = [];
    private readonly List<FoodOrderStatusEntry> _history = [];

    private FoodOrder()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerUserId { get; private set; }

    public Guid BusinessId { get; private set; }

    public FoodOrderStatus Status { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>When the restaurant must answer, or the customer must pay, by. Set only in those two states.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public string DeliveryArea { get; private set; } = string.Empty;

    public string DeliveryAddress { get; private set; } = string.Empty;

    public string? DeliveryNote { get; private set; }

    public string ContactPhone { get; private set; } = string.Empty;

    /// <summary>The restaurant's preparation time when the order was placed, in minutes.</summary>
    public int PrepTimeMinutes { get; private set; }

    /// <summary>Set when the kitchen starts, from <see cref="PrepTimeMinutes"/>.</summary>
    public DateTimeOffset? EstimatedReadyAt { get; private set; }

    public string Currency { get; private set; } = FoodPricing.Currency;

    public decimal Subtotal { get; private set; }

    public decimal DeliveryFee { get; private set; }

    public decimal PackagingFee { get; private set; }

    public decimal TaxAmount { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    /// <summary>Why the order was rejected or cancelled, when a reason was given.</summary>
    public string? ClosedReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<FoodOrderLine> Lines => _lines;

    public IReadOnlyList<FoodOrderStatusEntry> History => _history;

    /// <summary>Whether the order is still waiting on someone and has a deadline.</summary>
    public bool IsHolding => Status is FoodOrderStatus.Placed or FoodOrderStatus.AwaitingPayment;

    /// <summary>Whether the customer may still cancel (decision F8: until the restaurant starts preparing).</summary>
    public bool CanCustomerCancel => Status is FoodOrderStatus.Placed or FoodOrderStatus.AwaitingPayment or FoodOrderStatus.Confirmed;

    public static FoodOrder Place(
        Guid customerUserId,
        Guid businessId,
        string idempotencyKey,
        DeliveryDetails delivery,
        int prepTimeMinutes,
        OrderPrice price,
        DateTimeOffset now,
        TimeSpan acceptWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(idempotencyKey.Length, MaxKeyLength);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(acceptWindow, TimeSpan.Zero);
        if (price.Lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one dish.", nameof(price));
        }

        var order = new FoodOrder
        {
            Id = Guid.CreateVersion7(),
            CustomerUserId = customerUserId,
            BusinessId = businessId,
            Status = FoodOrderStatus.Placed,
            IdempotencyKey = idempotencyKey,
            ExpiresAt = now + acceptWindow,
            DeliveryArea = delivery.Area,
            DeliveryAddress = delivery.Address,
            DeliveryNote = delivery.Note,
            ContactPhone = delivery.ContactPhone,
            PrepTimeMinutes = prepTimeMinutes,
            Currency = price.Currency,
            Subtotal = price.Subtotal,
            DeliveryFee = price.DeliveryFee,
            PackagingFee = price.PackagingFee,
            TaxAmount = price.TaxAmount,
            DiscountAmount = price.DiscountAmount,
            TotalAmount = price.TotalAmount,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var line in price.Lines)
        {
            order._lines.Add(FoodOrderLine.Create(order.Id, line));
        }

        order.Record(null, FoodOrderStatus.Placed, FoodOrderActorType.Customer, customerUserId, null, now);
        return order;
    }

    /// <summary>
    /// The restaurant accepts. An order that has to be paid for online waits for payment (decision F6) with a fresh window; one that
    /// does not (online payment off, or nothing to pay) is confirmed straight away.
    /// </summary>
    public void Accept(Guid partnerUserId, DateTimeOffset now, bool requiresPayment, TimeSpan paymentWindow)
    {
        if (requiresPayment)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(paymentWindow, TimeSpan.Zero);
            Move(FoodOrderStatus.AwaitingPayment, FoodOrderActorType.Partner, partnerUserId, null, now, FoodOrderStatus.Placed);
            ExpiresAt = now + paymentWindow;
        }
        else
        {
            Move(FoodOrderStatus.Confirmed, FoodOrderActorType.Partner, partnerUserId, null, now, FoodOrderStatus.Placed);
        }
    }

    /// <summary>A verified payment arrived for an order waiting on it.</summary>
    public void ConfirmPayment(DateTimeOffset now) =>
        Move(FoodOrderStatus.Confirmed, FoodOrderActorType.System, null, null, now, FoodOrderStatus.AwaitingPayment);

    public void Reject(Guid partnerUserId, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Move(FoodOrderStatus.Rejected, FoodOrderActorType.Partner, partnerUserId, reason, now, FoodOrderStatus.Placed, FoodOrderStatus.AwaitingPayment);
    }

    public void StartPreparing(Guid partnerUserId, DateTimeOffset now)
    {
        Move(FoodOrderStatus.Preparing, FoodOrderActorType.Partner, partnerUserId, null, now, FoodOrderStatus.Confirmed);
        EstimatedReadyAt = now.AddMinutes(PrepTimeMinutes);
    }

    public void MarkReady(Guid partnerUserId, DateTimeOffset now) =>
        Move(FoodOrderStatus.ReadyForPickup, FoodOrderActorType.Partner, partnerUserId, null, now, FoodOrderStatus.Preparing);

    /// <summary>The driver collected the food (step 8.7).</summary>
    public void PickUp(Guid driverUserId, DateTimeOffset now) =>
        Move(FoodOrderStatus.OutForDelivery, FoodOrderActorType.Driver, driverUserId, null, now, FoodOrderStatus.ReadyForPickup);

    /// <summary>The driver handed it over (step 8.7).</summary>
    public void Deliver(Guid driverUserId, DateTimeOffset now) =>
        Move(FoodOrderStatus.Delivered, FoodOrderActorType.Driver, driverUserId, null, now, FoodOrderStatus.OutForDelivery);

    /// <summary>
    /// A cancellation. The customer may cancel until the restaurant starts preparing; the restaurant may cancel a confirmed order
    /// up to the point it leaves (a waiting order is rejected instead); an administrator may cancel anything that has not arrived.
    /// </summary>
    public void Cancel(FoodOrderActorType actorType, Guid? actorUserId, string? reason, DateTimeOffset now)
    {
        FoodOrderStatus[] allowed = actorType switch
        {
            FoodOrderActorType.Customer => [FoodOrderStatus.Placed, FoodOrderStatus.AwaitingPayment, FoodOrderStatus.Confirmed],
            FoodOrderActorType.Partner => [FoodOrderStatus.Confirmed, FoodOrderStatus.Preparing, FoodOrderStatus.ReadyForPickup],
            FoodOrderActorType.Admin =>
            [
                FoodOrderStatus.Placed,
                FoodOrderStatus.AwaitingPayment,
                FoodOrderStatus.Confirmed,
                FoodOrderStatus.Preparing,
                FoodOrderStatus.ReadyForPickup,
                FoodOrderStatus.OutForDelivery,
            ],
            _ => [],
        };

        if (actorType == FoodOrderActorType.Customer && Status is FoodOrderStatus.Preparing or FoodOrderStatus.ReadyForPickup or FoodOrderStatus.OutForDelivery)
        {
            throw new InvalidOperationException("The restaurant has already started on this order, so it can no longer be cancelled here. Contact support.");
        }

        Move(FoodOrderStatus.Cancelled, actorType, actorUserId, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), now, allowed);
    }

    /// <summary>The worker closes an order nobody answered or paid for in time.</summary>
    public void Expire(DateTimeOffset now) =>
        Move(FoodOrderStatus.Expired, FoodOrderActorType.System, null, null, now, FoodOrderStatus.Placed, FoodOrderStatus.AwaitingPayment);

    /// <summary>The gateway reported a verified failure.</summary>
    public void Fail(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Move(FoodOrderStatus.Failed, FoodOrderActorType.System, null, reason, now, FoodOrderStatus.AwaitingPayment);
    }

    /// <summary>A captured payment needs refunding after a rejection or cancellation (step 8.3).</summary>
    public void RequestRefund(FoodOrderActorType actorType, Guid? actorUserId, DateTimeOffset now) =>
        Move(FoodOrderStatus.RefundPending, actorType, actorUserId, null, now, FoodOrderStatus.Rejected, FoodOrderStatus.Cancelled);

    public void MarkRefunded(DateTimeOffset now) =>
        Move(FoodOrderStatus.Refunded, FoodOrderActorType.System, null, null, now, FoodOrderStatus.RefundPending);

    private void Move(
        FoodOrderStatus next,
        FoodOrderActorType actorType,
        Guid? actorUserId,
        string? reason,
        DateTimeOffset now,
        params ReadOnlySpan<FoodOrderStatus> allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
        {
            throw new InvalidOperationException($"An order cannot move from {Status} to {next}.");
        }

        var previous = Status;
        Status = next;
        if (!IsHolding)
        {
            ExpiresAt = null;
        }

        if (next is FoodOrderStatus.Cancelled or FoodOrderStatus.Rejected)
        {
            ClosedReason = reason;
        }

        UpdatedAt = now;
        Record(previous, next, actorType, actorUserId, reason, now);
    }

    private void Record(FoodOrderStatus? previous, FoodOrderStatus next, FoodOrderActorType actorType, Guid? actorUserId, string? reason, DateTimeOffset now) =>
        _history.Add(new FoodOrderStatusEntry(Guid.CreateVersion7(), Id, previous, next, actorType, actorUserId, reason, now));
}

/// <summary>One dish on an order. Written once with the order and never changed.</summary>
public sealed class FoodOrderLine
{
    public const int MaxQuantity = 20;

    private FoodOrderLine()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid MenuItemId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsVeg { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public decimal TaxRatePercent { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal TaxAmount { get; private set; }

    internal static FoodOrderLine Create(Guid orderId, PricedLine line) => new()
    {
        Id = Guid.CreateVersion7(),
        OrderId = orderId,
        MenuItemId = line.MenuItemId,
        Name = line.Name,
        IsVeg = line.IsVeg,
        UnitPrice = line.UnitPrice,
        Quantity = line.Quantity,
        TaxRatePercent = line.TaxRatePercent,
        Subtotal = line.Subtotal,
        TaxAmount = line.TaxAmount,
    };
}

/// <summary>One step in an order's life. Append-only: the database refuses edits and deletes.</summary>
public sealed class FoodOrderStatusEntry
{
    private FoodOrderStatusEntry()
    {
    }

    internal FoodOrderStatusEntry(
        Guid id,
        Guid orderId,
        FoodOrderStatus? previousStatus,
        FoodOrderStatus newStatus,
        FoodOrderActorType actorType,
        Guid? actorUserId,
        string? reason,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrderId = orderId;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        ActorType = actorType;
        ActorUserId = actorUserId;
        Reason = reason;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public FoodOrderStatus? PreviousStatus { get; private set; }

    public FoodOrderStatus NewStatus { get; private set; }

    public FoodOrderActorType ActorType { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
