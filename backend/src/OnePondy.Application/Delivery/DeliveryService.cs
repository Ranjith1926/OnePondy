using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Food;
using OnePondy.Application.Payments;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Food;

namespace OnePondy.Application.Delivery;

/// <summary>
/// Delivering food orders (spec Phase 8, step 8.7). A job opens when the restaurant marks an order ready (see
/// <c>OrderService</c>); any approved driver on duty may take an open one, and the first to accept gets it: two requests for the same
/// job collide on its row version (and the database refuses to reassign a taken job), so exactly one succeeds and the other is told it
/// was taken. A driver holds one job at a time (a unique index). The driver's position is accepted only for their own active job,
/// kept as the latest only, and erased when the job ends.
/// </summary>
public sealed class DeliveryService(IOnePondyDbContext db, TimeProvider time, LedgerService ledger, IOptions<FoodOptions> food)
{
    /// <summary>The most open jobs shown to a driver at once, oldest first.</summary>
    public const int MaxOffers = 20;

    // --- The driver ---

    public async Task<DriverJobsView> GetMineAsync(Guid driverUserId, CancellationToken cancellationToken)
    {
        var current = await CurrentAsync(driverUserId, tracked: false, cancellationToken);
        var available = await IsAvailableAsync(driverUserId, cancellationToken);
        if (current is not null)
        {
            return new DriverJobsView(await ToDetailAsync(current, cancellationToken), [], available);
        }

        if (!available)
        {
            return new DriverJobsView(null, [], false);
        }

        var open = await db.DeliveryJobs.AsNoTracking()
            .Where(j => j.Status == DeliveryJobStatus.Open)
            .OrderBy(j => j.OpenedAt)
            .Take(MaxOffers)
            .ToListAsync(cancellationToken);
        return new DriverJobsView(null, await ToOffersAsync(open, cancellationToken), true);
    }

    public async Task<Result<JobDetailView>> AcceptAsync(Guid driverUserId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await IsAvailableAsync(driverUserId, cancellationToken))
        {
            return DeliveryErrors.NotAvailable;
        }

        var job = await db.DeliveryJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            return DeliveryErrors.NotFound;
        }

        // A second tap on "accept" by the driver who already has it is not an error.
        if (job.Status == DeliveryJobStatus.Accepted && job.DriverUserId == driverUserId)
        {
            return await ToDetailAsync(job, cancellationToken);
        }

        if (job.Status != DeliveryJobStatus.Open)
        {
            return DeliveryErrors.Taken;
        }

        if (await CurrentAsync(driverUserId, tracked: false, cancellationToken) is not null)
        {
            return DriverErrors.HasActiveJob;
        }

        var order = await db.FoodOrders.AsNoTracking().SingleAsync(o => o.Id == job.FoodOrderId, cancellationToken);
        if (order.Status != FoodOrderStatus.ReadyForPickup)
        {
            return DeliveryErrors.Changed;
        }

        // A driver already holding as much cash as they may is not given another cash order until they hand some in (step 8.8).
        if (order.PaymentMethod == PaymentMethod.CashOnDelivery
            && await DriverCashBook.HeldAsync(db, driverUserId, cancellationToken) + order.TotalAmount > food.Value.CashOnDelivery.MaxDriverCashHeld)
        {
            return DeliveryErrors.CashLimit;
        }

        job.Accept(driverUserId, time.GetUtcNow());
        var saved = await SaveAsync(job, DeliveryErrors.Taken, cancellationToken);
        if (saved.IsSuccess || saved.Error != DeliveryErrors.Taken)
        {
            return saved;
        }

        // Lost the race. Either another driver got this job, or this driver accepted two jobs at the same moment and the database
        // allowed only one of them (a driver holds one at a time); say which.
        return await CurrentAsync(driverUserId, tracked: false, cancellationToken) is not null ? DriverErrors.HasActiveJob : DeliveryErrors.Taken;
    }

    /// <summary>Gives the delivery back before collecting the food, so another driver can take it.</summary>
    public Task<Result<JobDetailView>> ReleaseAsync(Guid driverUserId, Guid jobId, CancellationToken cancellationToken) =>
        MineAsync(driverUserId, jobId, job => job.Release(driverUserId, time.GetUtcNow()), orderStep: null, cancellationToken);

    /// <summary>The driver has the food: the job and the order both move on, in one save.</summary>
    public Task<Result<JobDetailView>> PickUpAsync(Guid driverUserId, Guid jobId, CancellationToken cancellationToken) =>
        MineAsync(
            driverUserId,
            jobId,
            job => job.PickUp(driverUserId, time.GetUtcNow()),
            order => order.PickUp(driverUserId, time.GetUtcNow()),
            cancellationToken);

    /// <summary>
    /// The driver handed it over: the job and the order are finished, and the driver's position is erased. For cash on delivery the driver
    /// confirms the cash they took, which must be exactly the total; the cash is then posted to the ledger as theirs to hand in, in the same
    /// save. An online order has no cash to confirm.
    /// </summary>
    public async Task<Result<JobDetailView>> DeliverAsync(Guid driverUserId, Guid jobId, decimal? cashCollected, CancellationToken cancellationToken)
    {
        var job = await db.DeliveryJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId && j.DriverUserId == driverUserId, cancellationToken);
        if (job is null)
        {
            return DeliveryErrors.NotFound;
        }

        var order = await db.FoodOrders.AsNoTracking().SingleAsync(o => o.Id == job.FoodOrderId, cancellationToken);
        var errors = new FieldErrors();
        if (order.PaymentMethod == PaymentMethod.CashOnDelivery && cashCollected != order.TotalAmount)
        {
            errors.Add("cashCollected", $"Confirm the exact cash you collected: ₹{order.TotalAmount:0.00}.");
        }
        else if (order.PaymentMethod != PaymentMethod.CashOnDelivery && cashCollected is not null)
        {
            errors.Add("cashCollected", "This order was paid online, so there is no cash to collect.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        return await MineAsync(
            driverUserId,
            jobId,
            j => j.Deliver(driverUserId, time.GetUtcNow()),
            o => o.Deliver(driverUserId, time.GetUtcNow(), cashCollected),
            cancellationToken,
            afterOrderStep: o => o.PaymentMethod == PaymentMethod.CashOnDelivery ? ledger.PostFoodCashAsync(o, cancellationToken) : Task.CompletedTask);
    }

    /// <summary>Stores the driver's latest position, only while they hold the job.</summary>
    public async Task<Result<bool>> RecordLocationAsync(Guid driverUserId, Guid jobId, LocationInput input, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        if (input.Latitude is not { } latitude || !double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            errors.Add("latitude", "Latitude must be between -90 and 90.");
        }

        if (input.Longitude is not { } longitude || !double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            errors.Add("longitude", "Longitude must be between -180 and 180.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var job = await db.DeliveryJobs.SingleOrDefaultAsync(j => j.Id == jobId && j.DriverUserId == driverUserId, cancellationToken);
        if (job is null)
        {
            return DeliveryErrors.NotFound;
        }

        try
        {
            job.RecordLocation(driverUserId, input.Latitude!.Value, input.Longitude!.Value, time.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return DeliveryErrors.InvalidTransition(exception.Message);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            db.ClearTracked();
            return DeliveryErrors.Changed;
        }

        return true;
    }

    // --- Shared ---

    /// <summary>One step on the driver's own job, with the order's matching step when there is one, saved together.</summary>
    private async Task<Result<JobDetailView>> MineAsync(
        Guid driverUserId,
        Guid jobId,
        Action<DeliveryJob> jobStep,
        Action<FoodOrder>? orderStep,
        CancellationToken cancellationToken,
        Func<FoodOrder, Task>? afterOrderStep = null)
    {
        var job = await db.DeliveryJobs.SingleOrDefaultAsync(j => j.Id == jobId && j.DriverUserId == driverUserId, cancellationToken);
        if (job is null)
        {
            return DeliveryErrors.NotFound;
        }

        FoodOrder? order = null;
        var historyBefore = 0;
        if (orderStep is not null)
        {
            order = await db.FoodOrders.Include(o => o.History).SingleAsync(o => o.Id == job.FoodOrderId, cancellationToken);
            historyBefore = order.History.Count;
        }

        try
        {
            jobStep(job);
            if (order is not null)
            {
                orderStep!(order);
                if (afterOrderStep is not null)
                {
                    await afterOrderStep(order);
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            db.ClearTracked();
            return DeliveryErrors.InvalidTransition(exception.Message);
        }

        if (order is not null)
        {
            for (var i = historyBefore; i < order.History.Count; i++)
            {
                db.MarkAdded(order.History[i]);
            }
        }

        return await SaveAsync(job, DeliveryErrors.Changed, cancellationToken);
    }

    private async Task<Result<JobDetailView>> SaveAsync(DeliveryJob job, AppError onConflict, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // Someone else (another driver, the restaurant cancelling, an administrator) changed it at the same moment; nothing of ours was saved.
            db.ClearTracked();
            return onConflict;
        }

        return await ToDetailAsync(job, cancellationToken);
    }

    private Task<DeliveryJob?> CurrentAsync(Guid driverUserId, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? db.DeliveryJobs : db.DeliveryJobs.AsNoTracking();
        return query.SingleOrDefaultAsync(
            j => j.DriverUserId == driverUserId && (j.Status == DeliveryJobStatus.Accepted || j.Status == DeliveryJobStatus.PickedUp),
            cancellationToken);
    }

    private async Task<bool> IsAvailableAsync(Guid driverUserId, CancellationToken cancellationToken)
    {
        var profile = await db.DriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == driverUserId, cancellationToken);
        return profile is { IsAvailableForJobs: true };
    }

    private async Task<List<JobOfferView>> ToOffersAsync(List<DeliveryJob> jobs, CancellationToken cancellationToken)
    {
        var orderIds = jobs.Select(j => j.FoodOrderId).ToList();
        var orders = await db.FoodOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var counts = await db.FoodOrderLines.AsNoTracking()
            .Where(l => orderIds.Contains(l.OrderId))
            .GroupBy(l => l.OrderId)
            .Select(g => new { OrderId = g.Key, Count = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(r => r.OrderId, r => r.Count, cancellationToken);
        var businessIds = orders.Values.Select(o => o.BusinessId).Distinct().ToList();
        var businesses = await db.Businesses.AsNoTracking().Where(b => businessIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);

        return
        [
            .. jobs.Select(j =>
            {
                var order = orders[j.FoodOrderId];
                var business = businesses[order.BusinessId];
                return new JobOfferView(
                    j.Id,
                    business.Name,
                    business.Address,
                    order.DeliveryArea,
                    counts.GetValueOrDefault(order.Id),
                    j.OpenedAt,
                    order.PaymentMethod,
                    order.PaymentMethod == PaymentMethod.CashOnDelivery ? order.TotalAmount : null);
            }),
        ];
    }

    private async Task<JobDetailView> ToDetailAsync(DeliveryJob job, CancellationToken cancellationToken)
    {
        var order = await db.FoodOrders.AsNoTracking().SingleAsync(o => o.Id == job.FoodOrderId, cancellationToken);
        var business = await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == order.BusinessId, cancellationToken);
        var items = await db.FoodOrderLines.AsNoTracking().Where(l => l.OrderId == order.Id).ToListAsync(cancellationToken);
        return new JobDetailView(
            job.Id,
            job.Status,
            business.Name,
            business.Address,
            business.PhoneNumber,
            order.DeliveryArea,
            order.DeliveryAddress,
            order.DeliveryNote,
            order.ContactPhone,
            [.. items.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Select(l => new JobItemView(l.Name, l.Quantity))],
            job.AcceptedAt,
            job.PickedUpAt,
            order.PaymentMethod,
            order.PaymentMethod == PaymentMethod.CashOnDelivery ? order.TotalAmount : null);
    }
}

/// <summary>Reads how deliveries are going, for the order screens of the customer and the restaurant.</summary>
public static class DeliveryProgress
{
    /// <summary>
    /// The progress of each given order's delivery. An order with no delivery, or whose delivery was cancelled, has no entry.
    /// <paramref name="forCustomer"/> is true only when the caller is the customer who placed the orders: then, while the driver is
    /// carrying the food, the driver's last reported position is included. It is never included for the restaurant.
    /// </summary>
    public static async Task<Dictionary<Guid, DeliveryProgressView>> ForOrdersAsync(
        IOnePondyDbContext db,
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken,
        bool forCustomer = false)
    {
        if (orderIds.Count == 0)
        {
            return [];
        }

        var jobs = await db.DeliveryJobs.AsNoTracking()
            .Where(j => orderIds.Contains(j.FoodOrderId) && j.Status != DeliveryJobStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var driverIds = jobs.Where(j => j.DriverUserId is not null).Select(j => j.DriverUserId!.Value).Distinct().ToList();
        var drivers = await db.DriverProfiles.AsNoTracking().Where(p => driverIds.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, cancellationToken);

        return jobs.ToDictionary(
            j => j.FoodOrderId,
            j =>
            {
                var driver = j.DriverUserId is { } id ? drivers.GetValueOrDefault(id) : null;
                var first = driver?.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                var position = forCustomer
                    && j.Status == DeliveryJobStatus.PickedUp
                    && j.LastLatitude is { } latitude
                    && j.LastLongitude is { } longitude
                    ? new DeliveryPosition(latitude, longitude)
                    : null;
                return new DeliveryProgressView(j.Status, first, driver?.VehicleType, j.LastLocationAt, position);
            });
    }
}
