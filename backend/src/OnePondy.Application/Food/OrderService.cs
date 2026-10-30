using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Bookings;
using OnePondy.Application.Common;
using OnePondy.Application.Payments;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Food;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Food;

/// <summary>
/// Food orders (spec Phase 8, steps 8.2 and 8.3). The server prices every cart from the menu, revalidates the price when the order is
/// placed, and is the only thing that moves an order from one state to the next. An order that needs online payment waits in
/// <c>awaiting_payment</c> until <see cref="PaymentService"/> confirms it; cancelling a paid order refunds it in full (decision F8).
/// </summary>
public sealed partial class OrderService(
    IOnePondyDbContext db,
    RestaurantService restaurants,
    RefundService refunds,
    IOptions<FoodOptions> food,
    TimeProvider time)
{
    public const int MaxLines = 30;
    public const int MaxAddressLength = 300;
    public const int MaxNoteLength = 200;

    /// <summary>The most orders one list returns, newest first.</summary>
    public const int MaxListed = 100;

    [GeneratedRegex(@"^\+?[0-9]{10,13}$")]
    private static partial Regex PhonePattern();

    // --- Customer ---

    /// <summary>Prices a cart without placing it, so the app can show the full breakdown before the customer commits.</summary>
    public async Task<Result<OrderQuoteView>> QuoteAsync(QuoteInput input, CancellationToken cancellationToken)
    {
        var built = await BuildAsync(input.BusinessId, input.Area, input.Lines, cancellationToken);
        return built.IsSuccess ? ToQuoteView(built.Value) : built.Error;
    }

    public async Task<Result<OrderDetail>> PlaceAsync(Guid customerUserId, PlaceOrderInput input, CancellationToken cancellationToken)
    {
        var key = input.IdempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > FoodOrder.MaxKeyLength)
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["idempotencyKey"] = [$"Required, at most {FoodOrder.MaxKeyLength} characters."] });
        }

        var existing = await FindByKeyAsync(customerUserId, key, cancellationToken);
        if (existing is not null)
        {
            return await ToDetailAsync(existing, cancellationToken);
        }

        var open = await db.ModuleFlags.AsNoTracking()
            .AnyAsync(f => f.Key == ModuleKeys.Food && f.Status == ModuleStatus.Available, cancellationToken);
        if (!open)
        {
            return OrderErrors.ModuleOff;
        }

        var errors = new FieldErrors();
        var address = errors.Required("address", input.Address, MaxAddressLength);
        var note = errors.Optional("note", input.Note, MaxNoteLength);
        var phone = (input.ContactPhone ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        if (!PhonePattern().IsMatch(phone))
        {
            errors.Add("contactPhone", "Enter a phone number the driver can call.");
        }

        if (input.ExpectedTotal is null)
        {
            errors.Add("expectedTotal", "Required: send the total you showed the customer.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var built = await BuildAsync(input.BusinessId, input.Area, input.Lines, cancellationToken);
        if (!built.IsSuccess)
        {
            return built.Error;
        }

        if (input.ExpectedTotal != built.Value.Price.TotalAmount)
        {
            return OrderErrors.PriceChanged(ToQuoteView(built.Value));
        }

        var now = time.GetUtcNow();
        var order = FoodOrder.Place(
            customerUserId,
            input.BusinessId,
            key,
            new DeliveryDetails(built.Value.Area, address, note, phone),
            built.Value.Restaurant.Profile.PrepTimeMinutes,
            built.Value.Price,
            now,
            food.Value.AcceptWindow);
        db.FoodOrders.Add(order);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // The same attempt arrived twice at once: the database allows one, and the other gets that one back.
            db.ClearTracked();
            var raced = await FindByKeyAsync(customerUserId, key, cancellationToken);
            if (raced is not null)
            {
                return await ToDetailAsync(raced, cancellationToken);
            }

            throw;
        }

        return await ToDetailAsync(order, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderSummary>> ListMineAsync(Guid customerUserId, CancellationToken cancellationToken)
    {
        var orders = await db.FoodOrders.AsNoTracking()
            .Where(o => o.CustomerUserId == customerUserId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(MaxListed)
            .ToListAsync(cancellationToken);
        var counts = await ItemCountsAsync(orders, cancellationToken);
        var names = await RestaurantNamesAsync(orders, cancellationToken);
        return
        [
            .. orders.Select(o => new OrderSummary(
                o.Id, o.BusinessId, names[o.BusinessId], o.Status, o.ExpiresAt, counts.GetValueOrDefault(o.Id), o.TotalAmount, o.Currency, o.CreatedAt)),
        ];
    }

    public async Task<Result<OrderDetail>> GetMineAsync(Guid id, Guid customerUserId, CancellationToken cancellationToken)
    {
        var order = await Orders().AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        return order is null || order.CustomerUserId != customerUserId ? OrderErrors.NotFound : await ToDetailAsync(order, cancellationToken);
    }

    public async Task<Result<OrderDetail>> CancelMineAsync(Guid id, Guid customerUserId, string? reason, uint expectedVersion, CancellationToken cancellationToken)
    {
        var order = await Orders().SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null || order.CustomerUserId != customerUserId)
        {
            return OrderErrors.NotFound;
        }

        var failure = await TransitionAsync(
            order,
            expectedVersion,
            o => o.Cancel(FoodOrderActorType.Customer, customerUserId, reason, time.GetUtcNow()),
            cancellationToken,
            RefundReason.CustomerCancelled);
        return failure is null ? await ToDetailAsync(order, cancellationToken) : failure;
    }

    // --- Partner order inbox ---

    public async Task<Result<IReadOnlyList<PartnerOrderSummary>>> ListForBusinessAsync(
        Guid businessId, Guid ownerUserId, FoodOrderStatus? status, CancellationToken cancellationToken)
    {
        if (!await OwnsBusinessAsync(businessId, ownerUserId, cancellationToken))
        {
            return OrderErrors.NotFound;
        }

        var query = db.FoodOrders.AsNoTracking().Where(o => o.BusinessId == businessId);
        if (status is { } s)
        {
            query = query.Where(o => o.Status == s);
        }

        var orders = await query.OrderByDescending(o => o.CreatedAt).Take(MaxListed).ToListAsync(cancellationToken);
        var lines = await LinesAsync(orders, cancellationToken);
        return Result<IReadOnlyList<PartnerOrderSummary>>.Success(
        [
            .. orders.Select(o =>
            {
                var own = lines.GetValueOrDefault(o.Id) ?? [];
                return new PartnerOrderSummary(
                    o.Id,
                    o.Version,
                    o.Status,
                    o.ExpiresAt,
                    o.EstimatedReadyAt,
                    own.Sum(l => l.Quantity),
                    own,
                    o.TotalAmount,
                    o.Currency,
                    o.DeliveryArea,
                    o.CreatedAt);
            }),
        ]);
    }

    public async Task<Result<PartnerOrderDetail>> GetForBusinessAsync(Guid businessId, Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (!await OwnsBusinessAsync(businessId, ownerUserId, cancellationToken))
        {
            return OrderErrors.NotFound;
        }

        var order = await Orders().AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && o.BusinessId == businessId, cancellationToken);
        return order is null ? OrderErrors.NotFound : ToPartnerDetail(order);
    }

    /// <summary>
    /// Accepting moves the order to <c>awaiting_payment</c> with a fresh payment window while online payment is switched on; with
    /// payment off, or nothing to pay, it is confirmed straight away (the same rule as bookings).
    /// </summary>
    public async Task<Result<PartnerOrderDetail>> AcceptAsync(Guid businessId, Guid id, uint expectedVersion, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var paymentsOpen = await db.ModuleFlags.AsNoTracking()
            .AnyAsync(f => f.Key == ModuleKeys.Payments && f.Status == ModuleStatus.Available, cancellationToken);
        var window = food.Value.PaymentWindow;
        return await ForBusinessAsync(
            businessId, id, expectedVersion, ownerUserId, o => o.Accept(ownerUserId, time.GetUtcNow(), paymentsOpen && o.TotalAmount > 0m, window), cancellationToken);
    }

    public Task<Result<PartnerOrderDetail>> RejectAsync(Guid businessId, Guid id, uint expectedVersion, Guid ownerUserId, string? reason, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, FoodOrder.MaxReasonLength);
        return errors.Any
            ? Task.FromResult(Result<PartnerOrderDetail>.Failure(errors.ToError()))
            : ForBusinessAsync(businessId, id, expectedVersion, ownerUserId, o => o.Reject(ownerUserId, trimmed, time.GetUtcNow()), cancellationToken);
    }

    public Task<Result<PartnerOrderDetail>> StartPreparingAsync(Guid businessId, Guid id, uint expectedVersion, Guid ownerUserId, CancellationToken cancellationToken) =>
        ForBusinessAsync(businessId, id, expectedVersion, ownerUserId, o => o.StartPreparing(ownerUserId, time.GetUtcNow()), cancellationToken);

    public Task<Result<PartnerOrderDetail>> MarkReadyAsync(Guid businessId, Guid id, uint expectedVersion, Guid ownerUserId, CancellationToken cancellationToken) =>
        ForBusinessAsync(businessId, id, expectedVersion, ownerUserId, o => o.MarkReady(ownerUserId, time.GetUtcNow()), cancellationToken);

    public Task<Result<PartnerOrderDetail>> CancelForBusinessAsync(Guid businessId, Guid id, uint expectedVersion, Guid ownerUserId, string? reason, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, FoodOrder.MaxReasonLength);
        return errors.Any
            ? Task.FromResult(Result<PartnerOrderDetail>.Failure(errors.ToError()))
            : ForBusinessAsync(
                businessId,
                id,
                expectedVersion,
                ownerUserId,
                o => o.Cancel(FoodOrderActorType.Partner, ownerUserId, trimmed, time.GetUtcNow()),
                cancellationToken,
                RefundReason.PartnerCancelled);
    }

    // --- Worker ---

    /// <summary>
    /// Expires one order whose deadline has passed, loaded fresh. Called by <see cref="FoodOrderExpiry"/>, after it has asked the gateway
    /// about an order that was waiting for payment; not reachable through any API endpoint. False when it no longer needed expiring or
    /// someone else moved it at the same moment.
    /// </summary>
    public async Task<bool> TryExpireAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await Orders().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null || !order.IsHolding || order.ExpiresAt >= time.GetUtcNow())
        {
            return false;
        }

        return await TransitionAsync(order, order.Version, o => o.Expire(time.GetUtcNow()), cancellationToken) is null;
    }

    // --- Shared ---

    private sealed record Built(OpenRestaurantInfo Restaurant, string Area, OrderPrice Price);

    private IQueryable<FoodOrder> Orders() => db.FoodOrders.Include(o => o.Lines).Include(o => o.History);

    /// <summary>
    /// Checks a cart against the menu and prices it. Everything a client sends is a claim: the restaurant must be open and serve the
    /// area, every dish must be on this restaurant's menu and available, and the prices come from the menu, never from the client.
    /// </summary>
    private async Task<Result<Built>> BuildAsync(Guid businessId, string? area, IReadOnlyList<OrderLineInput> lines, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        if (area is null || !FoodAreas.IsKnown(area) || area == FoodAreas.WholeCity)
        {
            errors.Add("area", "Choose the area to deliver to.");
        }

        if (lines.Count == 0)
        {
            errors.Add("lines", "Add at least one dish.");
        }
        else if (lines.Any(l => l.Quantity < 1))
        {
            errors.Add("lines", "Each dish needs a quantity of at least 1.");
        }

        // The same dish twice is one line: quantities add up, in the order the dishes first appeared.
        var merged = lines
            .Where(l => l.Quantity >= 1)
            .GroupBy(l => l.MenuItemId)
            .Select(g => new OrderLineInput(g.Key, g.Sum(l => l.Quantity)))
            .ToList();
        if (merged.Count > MaxLines)
        {
            errors.Add("lines", $"An order can have at most {MaxLines} different dishes.");
        }

        if (merged.Any(l => l.Quantity > FoodOrderLine.MaxQuantity))
        {
            errors.Add("lines", $"You can order at most {FoodOrderLine.MaxQuantity} of one dish.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var restaurant = await restaurants.FindOpenAsync(businessId, cancellationToken);
        if (restaurant is null)
        {
            return OrderErrors.RestaurantClosed;
        }

        if (!FoodAreas.Serves(restaurant.Areas, area))
        {
            return OrderErrors.AreaNotServed;
        }

        var ids = merged.Select(l => l.MenuItemId).ToList();
        var items = await db.MenuItems.AsNoTracking()
            .Where(i => i.BusinessId == businessId && ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        if (merged.Any(l => !items.ContainsKey(l.MenuItemId)))
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["lines"] = ["Some dishes are not on this restaurant's menu."] });
        }

        var unavailable = merged.Select(l => items[l.MenuItemId]).Where(i => !i.IsAvailable || i.IsArchived).Select(i => i.Name).ToList();
        if (unavailable.Count > 0)
        {
            return OrderErrors.ItemUnavailable(unavailable);
        }

        var priced = merged.Select(l => FoodPricing.Line(items[l.MenuItemId], l.Quantity)).ToList();
        var price = FoodPricing.Total(priced, food.Value.DeliveryFee, food.Value.PackagingFee);
        if (price.Subtotal < restaurant.Profile.MinOrderAmount)
        {
            return OrderErrors.BelowMinimum(restaurant.Profile.MinOrderAmount);
        }

        if (price.TotalAmount > RestaurantProfile.MaxOrderAmount)
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["lines"] = ["This order is too large. Split it into smaller orders."] });
        }

        return new Built(restaurant, area!, price);
    }

    private async Task<Result<PartnerOrderDetail>> ForBusinessAsync(
        Guid businessId,
        Guid id,
        uint expectedVersion,
        Guid ownerUserId,
        Action<FoodOrder> apply,
        CancellationToken cancellationToken,
        RefundReason? cancelReason = null)
    {
        if (!await OwnsBusinessAsync(businessId, ownerUserId, cancellationToken))
        {
            return OrderErrors.NotFound;
        }

        var order = await Orders().SingleOrDefaultAsync(o => o.Id == id && o.BusinessId == businessId, cancellationToken);
        if (order is null)
        {
            return OrderErrors.NotFound;
        }

        var failure = await TransitionAsync(order, expectedVersion, apply, cancellationToken, cancelReason);
        return failure is null ? ToPartnerDetail(order) : failure;
    }

    /// <summary>
    /// Applies one state change if the caller saw the current version; returns the problem, or null when it was saved. A cancellation
    /// (<paramref name="cancelReason"/> set) of a paid order decides its refund first and saves it with the cancellation; any move out of
    /// waiting closes the order's open payment, in the same save, so a payment landing at that instant collides with it and is retried
    /// or refunded, never lost.
    /// </summary>
    private async Task<AppError?> TransitionAsync(
        FoodOrder order,
        uint expectedVersion,
        Action<FoodOrder> apply,
        CancellationToken cancellationToken,
        RefundReason? cancelReason = null)
    {
        if (order.Version != expectedVersion)
        {
            return OrderErrors.Changed;
        }

        // Decided before anything is changed, so a refusal (refunds switched off) leaves the order exactly as it was.
        RefundPlan? plan = null;
        if (cancelReason is not null)
        {
            var planned = await refunds.PlanFoodCancellationAsync(order, cancellationToken);
            if (!planned.IsSuccess)
            {
                return planned.Error;
            }

            plan = planned.Value;
        }

        db.ExpectVersion(order, expectedVersion);
        var historyBefore = order.History.Count;
        try
        {
            apply(order);
        }
        catch (InvalidOperationException exception)
        {
            return OrderErrors.InvalidTransition(exception.Message);
        }

        var now = time.GetUtcNow();
        Refund? refund = null;
        if (plan is not null && order.Status == FoodOrderStatus.Cancelled)
        {
            order.RequestRefund(FoodOrderActorType.System, null, now);
            refund = Refund.Of(plan.Attempt, plan.Amount, cancelReason!.Value, now);
            db.Refunds.Add(refund);
        }

        for (var i = historyBefore; i < order.History.Count; i++)
        {
            db.MarkAdded(order.History[i]);
        }

        if (order.Status is FoodOrderStatus.Cancelled or FoodOrderStatus.Rejected or FoodOrderStatus.Expired or FoodOrderStatus.Failed or FoodOrderStatus.RefundPending)
        {
            var open = await db.PaymentAttempts
                .Where(a => a.FoodOrderId == order.Id && a.Status == PaymentAttemptStatus.Created)
                .ToListAsync(cancellationToken);
            foreach (var attempt in open)
            {
                attempt.MarkExpired(now);
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // Someone else moved this order (or paid it, or refunded it) at the same moment; nothing of ours was saved.
            db.ClearTracked();
            return OrderErrors.Changed;
        }

        if (refund is not null)
        {
            // After the save, so the cancellation and the refund it owes are never separated; if the gateway cannot be reached the
            // refund is retried by the Worker.
            await refunds.SendAsync(refund, plan!.Attempt, cancellationToken);
        }

        return null;
    }

    private Task<FoodOrder?> FindByKeyAsync(Guid customerUserId, string key, CancellationToken cancellationToken) =>
        Orders().AsNoTracking().SingleOrDefaultAsync(o => o.CustomerUserId == customerUserId && o.IdempotencyKey == key, cancellationToken);

    private Task<bool> OwnsBusinessAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken) =>
        db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId && b.OwnerUserId == ownerUserId, cancellationToken);

    private async Task<Dictionary<Guid, int>> ItemCountsAsync(List<FoodOrder> orders, CancellationToken cancellationToken)
    {
        var ids = orders.Select(o => o.Id).ToList();
        var rows = await db.FoodOrderLines.AsNoTracking()
            .Where(l => ids.Contains(l.OrderId))
            .GroupBy(l => l.OrderId)
            .Select(g => new { OrderId = g.Key, Count = g.Sum(l => l.Quantity) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.OrderId, r => r.Count);
    }

    /// <summary>The dishes of each order, by name (a line has no stored position, so any other order would change from one read to the next).</summary>
    private async Task<Dictionary<Guid, List<OrderLineView>>> LinesAsync(List<FoodOrder> orders, CancellationToken cancellationToken)
    {
        var ids = orders.Select(o => o.Id).ToList();
        var rows = await db.FoodOrderLines.AsNoTracking()
            .Where(l => ids.Contains(l.OrderId))
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(l => l.OrderId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(l => l.MenuItemId)
                    .Select(l => new OrderLineView(l.MenuItemId, l.Name, l.IsVeg, l.UnitPrice, l.Quantity, l.TaxRatePercent, l.Subtotal, l.TaxAmount))
                    .ToList());
    }

    private async Task<Dictionary<Guid, string>> RestaurantNamesAsync(List<FoodOrder> orders, CancellationToken cancellationToken)
    {
        var ids = orders.Select(o => o.BusinessId).Distinct().ToList();
        return await db.Businesses.AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
    }

    private async Task<OrderDetail> ToDetailAsync(FoodOrder order, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == order.BusinessId, cancellationToken);

        // The receipt: the payment that confirmed the order is the first one captured (a later one would be a duplicate, refunded).
        var paid = await db.PaymentAttempts.AsNoTracking()
            .Where(a => a.FoodOrderId == order.Id && a.Status == PaymentAttemptStatus.Captured)
            .OrderBy(a => a.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var orderRefunds = await db.Refunds.AsNoTracking().Where(r => r.FoodOrderId == order.Id).OrderBy(r => r.RequestedAt).ToListAsync(cancellationToken);
        return new OrderDetail(
            order.Id,
            order.Version,
            order.BusinessId,
            business.Name,
            business.FssaiLicenseNumber,
            order.Status,
            order.CanCustomerCancel,
            order.ExpiresAt,
            order.EstimatedReadyAt,
            order.PrepTimeMinutes,
            order.DeliveryArea,
            order.DeliveryAddress,
            order.DeliveryNote,
            order.ContactPhone,
            ToLineViews(order),
            ToPriceView(order),
            order.ClosedReason,
            ToHistoryViews(order),
            order.CreatedAt,
            order.UpdatedAt,
            paid is null ? null : new PaymentView(paid.Amount, paid.Currency, paid.UpdatedAt, paid.ProviderPaymentId!),
            [.. orderRefunds.Select(r => new RefundView(r.Amount, r.Reason, r.Status, r.RequestedAt, r.ProcessedAt))]);
    }

    private static PartnerOrderDetail ToPartnerDetail(FoodOrder order) => new(
        order.Id,
        order.Version,
        order.Status,
        order.ExpiresAt,
        order.EstimatedReadyAt,
        order.PrepTimeMinutes,
        order.DeliveryArea,
        ToLineViews(order),
        ToPriceView(order),
        order.ClosedReason,
        ToHistoryViews(order),
        order.CreatedAt,
        order.UpdatedAt);

    private static OrderQuoteView ToQuoteView(Built built) => new(
        built.Restaurant.Business.Id,
        built.Restaurant.Business.Name,
        built.Area,
        built.Restaurant.Profile.PrepTimeMinutes,
        built.Restaurant.Profile.MinOrderAmount,
        [.. built.Price.Lines.Select(l => new OrderLineView(l.MenuItemId, l.Name, l.IsVeg, l.UnitPrice, l.Quantity, l.TaxRatePercent, l.Subtotal, l.TaxAmount))],
        new OrderPriceView(
            built.Price.Subtotal,
            built.Price.DeliveryFee,
            built.Price.PackagingFee,
            built.Price.TaxAmount,
            built.Price.DiscountAmount,
            built.Price.TotalAmount,
            built.Price.Currency));

    private static IReadOnlyList<OrderLineView> ToLineViews(FoodOrder order) =>
        [
            .. order.Lines
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.MenuItemId)
                .Select(l => new OrderLineView(l.MenuItemId, l.Name, l.IsVeg, l.UnitPrice, l.Quantity, l.TaxRatePercent, l.Subtotal, l.TaxAmount)),
        ];

    private static OrderPriceView ToPriceView(FoodOrder order) =>
        new(order.Subtotal, order.DeliveryFee, order.PackagingFee, order.TaxAmount, order.DiscountAmount, order.TotalAmount, order.Currency);

    private static IReadOnlyList<OrderHistoryView> ToHistoryViews(FoodOrder order) =>
        [.. order.History.OrderBy(h => h.CreatedAt).Select(h => new OrderHistoryView(h.PreviousStatus, h.NewStatus, h.ActorType, h.Reason, h.CreatedAt))];
}
