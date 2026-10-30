using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Payments;

/// <summary>
/// One payment attempt, for the console. No customer details: the booking or food order id (exactly one is set) is how support finds
/// the rest. A food order's "listing" is shown as "Food order" and its business is the restaurant.
/// </summary>
public sealed record PaymentRow(
    Guid Id,
    Guid? BookingId,
    string ListingTitle,
    Guid BusinessId,
    string BusinessName,
    decimal Amount,
    string Currency,
    PaymentAttemptStatus Status,
    string ProviderOrderId,
    string? ProviderPaymentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? FoodOrderId);

public sealed record RefundRow(
    Guid Id,
    Guid? BookingId,
    string ListingTitle,
    string BusinessName,
    decimal Amount,
    RefundReason Reason,
    RefundStatus Status,
    string? ProviderRefundId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ProcessedAt,
    Guid? FoodOrderId);

public sealed record PaymentQuery(PaymentAttemptStatus? Status, Guid? BusinessId, int? Page, int? PageSize);

public sealed record RefundQuery(RefundStatus? Status, int? Page, int? PageSize);

/// <summary>What the console shows of money in and out: every payment attempt and every refund, newest first (spec Phase 7).</summary>
public sealed class PaymentAdminService(IOnePondyDbContext db)
{
    public const string FoodOrderTitle = "Food order";

    public async Task<PagedResult<PaymentRow>> ListPaymentsAsync(PaymentQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var attempts = db.PaymentAttempts.AsNoTracking();
        if (query.Status is { } status)
        {
            attempts = attempts.Where(a => a.Status == status);
        }

        if (query.BusinessId is { } businessId)
        {
            var bookingIds = db.Bookings.Where(b => b.BusinessId == businessId).Select(b => b.Id);
            var orderIds = db.FoodOrders.Where(o => o.BusinessId == businessId).Select(o => o.Id);
            attempts = attempts.Where(a =>
                (a.BookingId != null && bookingIds.Contains(a.BookingId.Value)) || (a.FoodOrderId != null && orderIds.Contains(a.FoodOrderId.Value)));
        }

        var total = await attempts.CountAsync(cancellationToken);
        var rows = await attempts
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var context = await ContextAsync(rows.Select(a => (a.BookingId, a.FoodOrderId)), cancellationToken);
        var items = rows.Select(a =>
        {
            var about = context.GetValueOrDefault(a.BookingId ?? a.FoodOrderId!.Value);
            return new PaymentRow(
                a.Id,
                a.BookingId,
                about?.Title ?? string.Empty,
                about?.BusinessId ?? Guid.Empty,
                about?.BusinessName ?? string.Empty,
                a.Amount,
                a.Currency,
                a.Status,
                a.ProviderOrderId,
                a.ProviderPaymentId,
                a.CreatedAt,
                a.UpdatedAt,
                a.FoodOrderId);
        });
        return new PagedResult<PaymentRow>([.. items], page, pageSize, total);
    }

    public async Task<PagedResult<RefundRow>> ListRefundsAsync(RefundQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var refunds = db.Refunds.AsNoTracking();
        if (query.Status is { } status)
        {
            refunds = refunds.Where(r => r.Status == status);
        }

        var total = await refunds.CountAsync(cancellationToken);
        var rows = await refunds
            .OrderByDescending(r => r.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var context = await ContextAsync(rows.Select(r => (r.BookingId, r.FoodOrderId)), cancellationToken);
        var items = rows.Select(r =>
        {
            var about = context.GetValueOrDefault(r.BookingId ?? r.FoodOrderId!.Value);
            return new RefundRow(
                r.Id,
                r.BookingId,
                about?.Title ?? string.Empty,
                about?.BusinessName ?? string.Empty,
                r.Amount,
                r.Reason,
                r.Status,
                r.ProviderRefundId,
                r.RequestedAt,
                r.ProcessedAt,
                r.FoodOrderId);
        });
        return new PagedResult<RefundRow>([.. items], page, pageSize, total);
    }

    private sealed record Context(string Title, Guid BusinessId, string BusinessName);

    /// <summary>
    /// The listing (or "Food order") and business names for a page of rows, keyed by booking or food order id, in plain queries (as
    /// the booking lists do). The ids are version 7 GUIDs, so a booking and an order never share one.
    /// </summary>
    private async Task<Dictionary<Guid, Context>> ContextAsync(IEnumerable<(Guid? BookingId, Guid? FoodOrderId)> subjects, CancellationToken cancellationToken)
    {
        var list = subjects.ToList();
        var bookingIds = list.Where(s => s.BookingId != null).Select(s => s.BookingId!.Value).Distinct().ToList();
        var orderIds = list.Where(s => s.FoodOrderId != null).Select(s => s.FoodOrderId!.Value).Distinct().ToList();

        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => bookingIds.Contains(b.Id))
            .Select(b => new { b.Id, b.ListingId, b.BusinessId })
            .ToListAsync(cancellationToken);
        var orders = await db.FoodOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.BusinessId })
            .ToListAsync(cancellationToken);
        var listingIds = bookings.Select(b => b.ListingId).Distinct().ToList();
        var businessIds = bookings.Select(b => b.BusinessId).Concat(orders.Select(o => o.BusinessId)).Distinct().ToList();
        var titles = await db.Listings.AsNoTracking().Where(l => listingIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Title, cancellationToken);
        var names = await db.Businesses.AsNoTracking().Where(b => businessIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var context = bookings.ToDictionary(
            b => b.Id,
            b => new Context(titles.GetValueOrDefault(b.ListingId, string.Empty), b.BusinessId, names.GetValueOrDefault(b.BusinessId, string.Empty)));
        foreach (var order in orders)
        {
            context[order.Id] = new Context(FoodOrderTitle, order.BusinessId, names.GetValueOrDefault(order.BusinessId, string.Empty));
        }

        return context;
    }
}
