using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Payments;

/// <summary>One payment attempt, for the console. No customer details: the booking id is how support finds the rest.</summary>
public sealed record PaymentRow(
    Guid Id,
    Guid BookingId,
    string ListingTitle,
    Guid BusinessId,
    string BusinessName,
    decimal Amount,
    string Currency,
    PaymentAttemptStatus Status,
    string ProviderOrderId,
    string? ProviderPaymentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RefundRow(
    Guid Id,
    Guid BookingId,
    string ListingTitle,
    string BusinessName,
    decimal Amount,
    RefundReason Reason,
    RefundStatus Status,
    string? ProviderRefundId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ProcessedAt);

public sealed record PaymentQuery(PaymentAttemptStatus? Status, Guid? BusinessId, int? Page, int? PageSize);

public sealed record RefundQuery(RefundStatus? Status, int? Page, int? PageSize);

/// <summary>What the console shows of money in and out: every payment attempt and every refund, newest first (spec Phase 7).</summary>
public sealed class PaymentAdminService(IOnePondyDbContext db)
{
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
            attempts = attempts.Where(a => bookingIds.Contains(a.BookingId));
        }

        var total = await attempts.CountAsync(cancellationToken);
        var rows = await attempts
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var context = await ContextAsync(rows.Select(a => a.BookingId).Distinct().ToList(), cancellationToken);
        var items = rows.Select(a =>
        {
            var booking = context.GetValueOrDefault(a.BookingId);
            return new PaymentRow(
                a.Id,
                a.BookingId,
                booking?.ListingTitle ?? string.Empty,
                booking?.BusinessId ?? Guid.Empty,
                booking?.BusinessName ?? string.Empty,
                a.Amount,
                a.Currency,
                a.Status,
                a.ProviderOrderId,
                a.ProviderPaymentId,
                a.CreatedAt,
                a.UpdatedAt);
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

        var context = await ContextAsync(rows.Select(r => r.BookingId).Distinct().ToList(), cancellationToken);
        var items = rows.Select(r =>
        {
            var booking = context.GetValueOrDefault(r.BookingId);
            return new RefundRow(
                r.Id,
                r.BookingId,
                booking?.ListingTitle ?? string.Empty,
                booking?.BusinessName ?? string.Empty,
                r.Amount,
                r.Reason,
                r.Status,
                r.ProviderRefundId,
                r.RequestedAt,
                r.ProcessedAt);
        });
        return new PagedResult<RefundRow>([.. items], page, pageSize, total);
    }

    private sealed record BookingContext(string ListingTitle, Guid BusinessId, string BusinessName);

    /// <summary>The listing and business names for a page of rows, in three plain queries (as the booking lists do).</summary>
    private async Task<Dictionary<Guid, BookingContext>> ContextAsync(List<Guid> bookingIds, CancellationToken cancellationToken)
    {
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => bookingIds.Contains(b.Id))
            .Select(b => new { b.Id, b.ListingId, b.BusinessId })
            .ToListAsync(cancellationToken);
        var listingIds = bookings.Select(b => b.ListingId).Distinct().ToList();
        var businessIds = bookings.Select(b => b.BusinessId).Distinct().ToList();
        var titles = await db.Listings.AsNoTracking().Where(l => listingIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Title, cancellationToken);
        var names = await db.Businesses.AsNoTracking().Where(b => businessIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        return bookings.ToDictionary(
            b => b.Id,
            b => new BookingContext(titles.GetValueOrDefault(b.ListingId, string.Empty), b.BusinessId, names.GetValueOrDefault(b.BusinessId, string.Empty)));
    }
}
