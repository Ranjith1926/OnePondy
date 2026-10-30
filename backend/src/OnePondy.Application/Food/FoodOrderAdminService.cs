using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Food;

namespace OnePondy.Application.Food;

/// <summary>
/// Administrators' view of food orders and deliveries (spec Phase 8, step 8.9): every order across restaurants, the ones that look
/// stuck, who has each delivery, and the two things an administrator may do about a stuck one: cancel the order (a paid one is refunded
/// in full) or, before the food is collected, offer the delivery to the other drivers again. Everything an administrator does is
/// audited, and so is looking at where a driver is.
/// </summary>
public sealed class FoodOrderAdminService(
    IOnePondyDbContext db,
    OrderService orders,
    IUserAccountStore accounts,
    AuditTrail audit,
    IOptions<FoodOptions> food,
    TimeProvider time)
{
    /// <summary>The most active orders looked at when filtering by "needs attention".</summary>
    public const int MaxWatched = 500;

    public async Task<PagedResult<AdminOrderSummary>> ListAsync(AdminOrderListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var now = time.GetUtcNow();
        var limits = food.Value.Attention;

        if (query.NeedsAttention == true)
        {
            // Only orders still in progress can need attention; work out which do, oldest first, and page through those.
            var watched = db.FoodOrders.AsNoTracking().Where(o => OrderAttentionRules.Watched.Contains(o.Status));
            if (query.Status is { } only)
            {
                watched = watched.Where(o => o.Status == only);
            }

            var candidates = await watched.OrderBy(o => o.UpdatedAt).Take(MaxWatched).ToListAsync(cancellationToken);
            var jobs = await JobsAsync(candidates, cancellationToken);
            var flagged = candidates
                .Select(o => (Order: o, Attention: OrderAttentionRules.For(o, jobs.GetValueOrDefault(o.Id), limits, now)))
                .Where(x => x.Attention is not null)
                .ToList();
            var slice = flagged.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return new PagedResult<AdminOrderSummary>(
                await ToSummariesAsync([.. slice.Select(x => x.Order)], jobs, limits, now, cancellationToken),
                page,
                pageSize,
                flagged.Count);
        }

        var all = db.FoodOrders.AsNoTracking();
        if (query.Status is { } status)
        {
            all = all.Where(o => o.Status == status);
        }

        var total = await all.CountAsync(cancellationToken);
        var rows = await all.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var rowJobs = await JobsAsync(rows, cancellationToken);
        return new PagedResult<AdminOrderSummary>(await ToSummariesAsync(rows, rowJobs, limits, now, cancellationToken), page, pageSize, total);
    }

    /// <summary>How many orders need attention right now, for a badge in the console.</summary>
    public async Task<int> AttentionCountAsync(CancellationToken cancellationToken) =>
        (await ListAsync(new AdminOrderListQuery(null, true, 1, 1), cancellationToken)).TotalCount;

    public async Task<Result<AdminOrderDetail>> GetAsync(Guid id, Guid adminUserId, CancellationToken cancellationToken)
    {
        var order = await orders.GetForAdminAsync(id, cancellationToken);
        if (!order.IsSuccess)
        {
            return order.Error;
        }

        var entity = await db.FoodOrders.AsNoTracking().SingleAsync(o => o.Id == id, cancellationToken);
        var job = await db.DeliveryJobs.AsNoTracking().SingleOrDefaultAsync(j => j.FoodOrderId == id, cancellationToken);

        // Where a driver is, is shown to administrators only, only while the delivery is under way, and every look is on the record.
        var showsPosition = job is { IsActive: true, LastLatitude: not null };
        if (showsPosition)
        {
            audit.Record(adminUserId, AuditActions.DriverLocationViewed, AuditEntityTypes.FoodOrder, id, new { job!.DriverUserId });
            await db.SaveChangesAsync(cancellationToken);
        }

        return new AdminOrderDetail(
            order.Value,
            job is null ? null : await ToDeliveryAsync(job, cancellationToken),
            OrderAttentionRules.For(entity, job, food.Value.Attention, time.GetUtcNow()),
            entity.Status is FoodOrderStatus.Placed or FoodOrderStatus.AwaitingPayment or FoodOrderStatus.Confirmed or FoodOrderStatus.Preparing
                or FoodOrderStatus.ReadyForPickup or FoodOrderStatus.OutForDelivery);
    }

    public async Task<Result<AdminOrderDetail>> CancelAsync(Guid id, uint version, string? reason, Guid adminUserId, CancellationToken cancellationToken)
    {
        var cancelled = await orders.CancelAsAdminAsync(
            id,
            version,
            adminUserId,
            reason,
            order => audit.Record(
                adminUserId,
                AuditActions.FoodOrderCancelledByAdmin,
                AuditEntityTypes.FoodOrder,
                order.Id,
                new { reason = reason?.Trim(), previousStatus = order.History.Last(h => h.NewStatus == FoodOrderStatus.Cancelled).PreviousStatus.ToString() }),
            cancellationToken);
        return cancelled.IsSuccess ? await GetAsync(id, adminUserId, cancellationToken) : cancelled.Error;
    }

    /// <summary>Offers an uncollected delivery to the other drivers again, taking it from the driver who has it.</summary>
    public async Task<Result<AdminOrderDetail>> ReleaseDeliveryAsync(Guid orderId, uint jobVersion, Guid adminUserId, CancellationToken cancellationToken)
    {
        var job = await db.DeliveryJobs.SingleOrDefaultAsync(j => j.FoodOrderId == orderId, cancellationToken);
        if (job is null)
        {
            return OrderErrors.NotFound;
        }

        if (job.Version != jobVersion)
        {
            return AdminOrderErrors.JobChanged;
        }

        if (job.Status != DeliveryJobStatus.Accepted)
        {
            return AdminOrderErrors.NothingToRelease;
        }

        var driver = job.DriverUserId;
        db.ExpectVersion(job, jobVersion);
        job.AdminRelease(time.GetUtcNow());
        audit.Record(adminUserId, AuditActions.DeliveryReleasedByAdmin, AuditEntityTypes.FoodOrder, orderId, new { fromDriver = driver });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // The driver collected it, or the order was cancelled, at the same moment.
            db.ClearTracked();
            return AdminOrderErrors.JobChanged;
        }

        return await GetAsync(orderId, adminUserId, cancellationToken);
    }

    // --- Shared ---

    private async Task<Dictionary<Guid, DeliveryJob>> JobsAsync(List<FoodOrder> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(o => o.Id).ToList();
        return await db.DeliveryJobs.AsNoTracking().Where(j => ids.Contains(j.FoodOrderId)).ToDictionaryAsync(j => j.FoodOrderId, cancellationToken);
    }

    private async Task<List<AdminOrderSummary>> ToSummariesAsync(
        List<FoodOrder> rows,
        Dictionary<Guid, DeliveryJob> jobs,
        AttentionOptions limits,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ids = rows.Select(o => o.Id).ToList();
        var counts = await db.FoodOrderLines.AsNoTracking()
            .Where(l => ids.Contains(l.OrderId))
            .GroupBy(l => l.OrderId)
            .Select(g => new { OrderId = g.Key, Count = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(r => r.OrderId, r => r.Count, cancellationToken);
        var businessIds = rows.Select(o => o.BusinessId).Distinct().ToList();
        var names = await db.Businesses.AsNoTracking().Where(b => businessIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        var driverIds = jobs.Values.Where(j => j.DriverUserId is not null).Select(j => j.DriverUserId!.Value).Distinct().ToList();
        var drivers = await db.DriverProfiles.AsNoTracking().Where(p => driverIds.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.FullName, cancellationToken);

        return
        [
            .. rows.Select(o =>
            {
                var job = jobs.GetValueOrDefault(o.Id);
                var name = job?.DriverUserId is { } d ? drivers.GetValueOrDefault(d) : null;
                return new AdminOrderSummary(
                    o.Id,
                    o.Version,
                    o.Status,
                    names.GetValueOrDefault(o.BusinessId, string.Empty),
                    o.DeliveryArea,
                    counts.GetValueOrDefault(o.Id),
                    o.TotalAmount,
                    o.Currency,
                    o.CreatedAt,
                    o.UpdatedAt,
                    job?.Status,
                    name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                    OrderAttentionRules.For(o, job, limits, now));
            }),
        ];
    }

    private async Task<AdminDeliveryView> ToDeliveryAsync(DeliveryJob job, CancellationToken cancellationToken)
    {
        DriverProfile? profile = null;
        string? phone = null;
        if (job.DriverUserId is { } driverId)
        {
            profile = await db.DriverProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == driverId, cancellationToken);
            phone = (await accounts.GetProfileAsync(driverId, cancellationToken))?.PhoneNumber;
        }

        var position = job is { IsActive: true, LastLatitude: { } lat, LastLongitude: { } lng, LastLocationAt: { } at }
            ? new AdminDriverPosition(lat, lng, at)
            : null;
        return new AdminDeliveryView(
            job.Id,
            job.Version,
            job.Status,
            job.DriverUserId,
            profile?.FullName,
            phone,
            profile?.VehicleType,
            job.OpenedAt,
            job.AcceptedAt,
            job.PickedUpAt,
            job.DeliveredAt,
            job.CancelledAt,
            job.CancelReason,
            position,
            job.Status == DeliveryJobStatus.Accepted);
    }
}
