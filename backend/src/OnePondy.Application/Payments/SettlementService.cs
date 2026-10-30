using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Food;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Payments;

public sealed record SettlementSummary(
    Guid Id,
    Guid BusinessId,
    string BusinessName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal RefundAmount,
    decimal NetAmount,
    SettlementStatus Status,
    string? PayoutReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PaidAt,
    uint Version);

/// <summary>
/// One booking's or food order's line on a statement. No customer details: the id and the listing (or "Food order") are enough to find
/// it. Exactly one of <c>BookingId</c> and <c>FoodOrderId</c> is set.
/// </summary>
public sealed record SettlementItemView(
    Guid? BookingId,
    Guid? FoodOrderId,
    string ListingTitle,
    DateTimeOffset? ServiceStartAt,
    SettlementItemKind Kind,
    decimal GrossAmount,
    decimal CommissionAmount,
    /// <summary>What this line adds to (sale) or takes from (refund, negative) the partner's payout.</summary>
    decimal PartnerAmount);

public sealed record SettlementDetail(SettlementSummary Settlement, IReadOnlyList<SettlementItemView> Items);

public sealed record SettlementQuery(SettlementStatus? Status, Guid? BusinessId, int? Page, int? PageSize);

public static class SettlementErrors
{
    public static readonly AppError NotFound = new(
        "settlement_not_found",
        ErrorKind.NotFound,
        "That statement does not exist.");

    public static readonly AppError BusinessNotFound = new(
        "settlement_business_not_found",
        ErrorKind.NotFound,
        "That business does not exist.");

    public static readonly AppError NothingDue = new(
        "settlement_nothing_due",
        ErrorKind.Conflict,
        "Nothing is payable to this business for that period yet. Completed bookings, delivered food orders, or refunds on earlier statements, are what make a statement.");

    public static readonly AppError PeriodExists = new(
        "settlement_period_exists",
        ErrorKind.Conflict,
        "This business already has a statement for exactly that period.");

    public static readonly AppError WrongState = new(
        "settlement_invalid_state",
        ErrorKind.Conflict,
        "The statement is not in a state that allows that. Reload it and check its status.");

    public static readonly AppError Busy = new(
        "settlement_busy",
        ErrorKind.Conflict,
        "The statement changed at the same moment, or one of its bookings or orders went onto another statement. Reload and try again.");

    public static readonly AppError LedgerMismatch = new(
        "settlement_ledger_mismatch",
        ErrorKind.Conflict,
        "The statement does not agree with the ledger for one of its bookings or orders, so it was not created. Check the payments for that business.");
}

/// <summary>
/// Statements for partners (spec Phase 7). A statement is what one business is owed for the bookings that finished in a period:
/// the sales, the platform's commission, and any refunds. Every amount comes from the ledger, and a statement that does not
/// agree with it is refused, so a statement is a view of the books and never a second opinion.
/// <list type="bullet">
/// <item>A booking is on a statement once it is <c>completed</c>, or <c>refunded</c> after a partial refund (the partner keeps
/// what the customer was not given back). A fully refunded booking owes the partner nothing and never appears.</item>
/// <item>A food order is on a statement once it is <c>delivered</c>. Its line is the restaurant's side of the order (the dishes and
/// the packaging fee, less commission); the delivery fee and the GST are not the restaurant's. Food refunds are always in full, so a
/// refunded order is never on one.</item>
/// <item>Marking a statement paid posts the payout to the ledger (<c>partner_payable</c> down, <c>partner_paid</c> up).</item>
/// <item>A booking is a sale once and a refund at most once (a database rule), so no statement counts it twice. A refund that comes
/// after its sale was settled is carried on a later statement as an adjustment.</item>
/// </list>
/// Dates are UTC days. Payouts themselves happen outside the system (manual transfer, or Razorpay Route once approved); this only
/// records them.
/// </summary>
public sealed class SettlementService(IOnePondyDbContext db, LedgerService ledger, AuditTrail audit, TimeProvider time)
{
    /// <summary>A statement covers at most this many bookings; the rest wait for the next one.</summary>
    public const int MaxBookingsPerStatement = 500;

    // --- Administrators ---

    public async Task<PagedResult<SettlementSummary>> ListAsync(SettlementQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var settlements = db.Settlements.AsNoTracking();
        if (query.Status is { } status)
        {
            settlements = settlements.Where(s => s.Status == status);
        }

        if (query.BusinessId is { } businessId)
        {
            settlements = settlements.Where(s => s.BusinessId == businessId);
        }

        var total = await settlements.CountAsync(cancellationToken);
        var items = await settlements
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<SettlementSummary>(await SummariesAsync(items, cancellationToken), page, pageSize, total);
    }

    public async Task<Result<SettlementDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var settlement = await db.Settlements.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return settlement is null ? SettlementErrors.NotFound : await DetailAsync(settlement, cancellationToken);
    }

    /// <summary>Builds a draft statement for one business from everything finished by the end of the period and not yet on a statement.</summary>
    public async Task<Result<SettlementDetail>> DraftAsync(
        Guid businessId, DateOnly periodStart, DateOnly periodEnd, Guid staffUserId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var errors = new FieldErrors();
        if (periodEnd < periodStart)
        {
            errors.Add("periodEnd", "The period cannot end before it starts.");
        }

        if (periodEnd > today)
        {
            errors.Add("periodEnd", "A statement can only cover days that have finished.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        if (!await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId, cancellationToken))
        {
            return SettlementErrors.BusinessNotFound;
        }

        if (await db.Settlements.AsNoTracking().AnyAsync(
                s => s.BusinessId == businessId && s.PeriodStart == periodStart && s.PeriodEnd == periodEnd, cancellationToken))
        {
            return SettlementErrors.PeriodExists;
        }

        var cutoff = new DateTimeOffset(periodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var lines = await BuildLinesAsync(businessId, cutoff, cancellationToken);
        if (lines.Error is not null)
        {
            return lines.Error;
        }

        var sales = lines.Items.Where(i => i.Kind == SettlementItemKind.Sale).ToList();
        var refunds = lines.Items.Where(i => i.Kind == SettlementItemKind.Refund).ToList();
        var gross = sales.Sum(i => i.GrossAmount);
        var refunded = refunds.Sum(i => i.GrossAmount);
        var commission = sales.Sum(i => i.CommissionAmount) - refunds.Sum(i => i.CommissionAmount);
        if (lines.Items.Count == 0 || commission < 0m || gross - commission - refunded <= 0m)
        {
            return SettlementErrors.NothingDue;
        }

        var settlement = Settlement.Draft(businessId, periodStart, periodEnd, gross, commission, refunded, time.GetUtcNow());
        db.Settlements.Add(settlement);
        foreach (var line in lines.Items)
        {
            db.SettlementItems.Add(line.FoodOrderId is { } orderId
                ? SettlementItem.CreateForFoodOrder(settlement.Id, orderId, line.Kind, line.GrossAmount, line.CommissionAmount)
                : SettlementItem.Create(settlement.Id, line.BookingId!.Value, line.Kind, line.GrossAmount, line.CommissionAmount));
        }

        audit.Record(
            staffUserId,
            AuditActions.SettlementDrafted,
            AuditEntityTypes.Settlement,
            settlement.Id,
            new { businessId, periodStart, periodEnd, items = lines.Items.Count, net = settlement.NetAmount });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // The same period was drafted at the same moment, or a booking or order landed on another statement meanwhile.
            db.ClearTracked();
            return SettlementErrors.Busy;
        }

        return await GetAsync(settlement.Id, cancellationToken);
    }

    public async Task<Result<SettlementDetail>> ApproveAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken)
    {
        var settlement = await db.Settlements.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (settlement is null)
        {
            return SettlementErrors.NotFound;
        }

        if (settlement.Status != SettlementStatus.Draft)
        {
            return SettlementErrors.WrongState;
        }

        db.ExpectVersion(settlement, version);
        settlement.Approve(time.GetUtcNow());
        audit.Record(staffUserId, AuditActions.SettlementApproved, AuditEntityTypes.Settlement, settlement.Id, new { settlement.BusinessId, settlement.NetAmount });
        return await SaveThenAsync(settlement, cancellationToken);
    }

    /// <summary>The money has been sent: records the bank's reference and posts the payout to the ledger, in one save.</summary>
    public async Task<Result<SettlementDetail>> MarkPaidAsync(
        Guid id, uint version, string? payoutReference, Guid staffUserId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var reference = errors.Required("payoutReference", payoutReference, Settlement.MaxPayoutReferenceLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        var settlement = await db.Settlements.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (settlement is null)
        {
            return SettlementErrors.NotFound;
        }

        if (settlement.Status != SettlementStatus.Approved)
        {
            return SettlementErrors.WrongState;
        }

        db.ExpectVersion(settlement, version);
        settlement.MarkPaid(reference, time.GetUtcNow());
        ledger.PostPayout(settlement);
        audit.Record(
            staffUserId,
            AuditActions.SettlementPaid,
            AuditEntityTypes.Settlement,
            settlement.Id,
            new { settlement.BusinessId, settlement.NetAmount, payoutReference = reference });
        return await SaveThenAsync(settlement, cancellationToken);
    }

    /// <summary>Throws a draft away, so its bookings go back to waiting for the next statement. Only a draft: nothing approved is ever undone.</summary>
    public async Task<AppError?> DiscardDraftAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken)
    {
        var settlement = await db.Settlements.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (settlement is null)
        {
            return SettlementErrors.NotFound;
        }

        if (settlement.Status != SettlementStatus.Draft)
        {
            return SettlementErrors.WrongState;
        }

        db.ExpectVersion(settlement, version);
        var items = await db.SettlementItems.Where(i => i.SettlementId == id).ToListAsync(cancellationToken);
        db.SettlementItems.RemoveRange(items);
        db.Settlements.Remove(settlement);
        audit.Record(staffUserId, AuditActions.SettlementDiscarded, AuditEntityTypes.Settlement, id, new { settlement.BusinessId, settlement.PeriodStart, settlement.PeriodEnd });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            db.ClearTracked();
            return SettlementErrors.Busy;
        }
    }

    // --- Partners: their own approved and paid statements ---

    public async Task<Result<IReadOnlyList<SettlementSummary>>> ListForBusinessAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(businessId, ownerUserId, cancellationToken))
        {
            return SettlementErrors.NotFound;
        }

        var settlements = await db.Settlements.AsNoTracking()
            .Where(s => s.BusinessId == businessId && s.Status != SettlementStatus.Draft)
            .OrderByDescending(s => s.PeriodEnd)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<SettlementSummary>>.Success(await SummariesAsync(settlements, cancellationToken));
    }

    public async Task<Result<SettlementDetail>> GetForBusinessAsync(Guid businessId, Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var settlement = await FindVisibleToOwnerAsync(businessId, id, ownerUserId, cancellationToken);
        return settlement is null ? SettlementErrors.NotFound : await DetailAsync(settlement, cancellationToken);
    }

    /// <summary>The statement as a spreadsheet file (CSV), for the partner's own accounts.</summary>
    public async Task<Result<StatementFile>> CsvForBusinessAsync(Guid businessId, Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var settlement = await FindVisibleToOwnerAsync(businessId, id, ownerUserId, cancellationToken);
        if (settlement is null)
        {
            return SettlementErrors.NotFound;
        }

        var detail = await DetailAsync(settlement, cancellationToken);
        return new StatementFile($"statement-{detail.Settlement.PeriodStart:yyyy-MM-dd}-to-{detail.Settlement.PeriodEnd:yyyy-MM-dd}.csv", StatementCsv.Render(detail));
    }

    // --- Shared ---

    private async Task<Result<SettlementDetail>> SaveThenAsync(Settlement settlement, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // Another administrator changed the statement first (a stale version, or a second payout of the same statement).
            db.ClearTracked();
            return SettlementErrors.Busy;
        }

        return await GetAsync(settlement.Id, cancellationToken);
    }

    private Task<bool> OwnsAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken) =>
        db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId && b.OwnerUserId == ownerUserId, cancellationToken);

    private async Task<Settlement?> FindVisibleToOwnerAsync(Guid businessId, Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(businessId, ownerUserId, cancellationToken))
        {
            return null;
        }

        // A draft is still the administrators' working copy.
        return await db.Settlements.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id && s.BusinessId == businessId && s.Status != SettlementStatus.Draft, cancellationToken);
    }

    private async Task<IReadOnlyList<SettlementSummary>> SummariesAsync(List<Settlement> settlements, CancellationToken cancellationToken)
    {
        var businessIds = settlements.Select(s => s.BusinessId).Distinct().ToList();
        var names = await db.Businesses.AsNoTracking()
            .Where(b => businessIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        return [.. settlements.Select(s => ToSummary(s, names.GetValueOrDefault(s.BusinessId, string.Empty)))];
    }

    private async Task<SettlementDetail> DetailAsync(Settlement settlement, CancellationToken cancellationToken)
    {
        var businessName = await db.Businesses.AsNoTracking()
            .Where(b => b.Id == settlement.BusinessId)
            .Select(b => b.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
        var items = await db.SettlementItems.AsNoTracking().Where(i => i.SettlementId == settlement.Id).ToListAsync(cancellationToken);
        var bookingIds = items.Where(i => i.BookingId != null).Select(i => i.BookingId!.Value).ToList();
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => bookingIds.Contains(b.Id))
            .Select(b => new { b.Id, b.ListingId, b.StartAt })
            .ToDictionaryAsync(b => b.Id, cancellationToken);
        var listingIds = bookings.Values.Select(b => b.ListingId).Distinct().ToList();
        var titles = await db.Listings.AsNoTracking()
            .Where(l => listingIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Title, cancellationToken);

        // A delivered order's date is when it was delivered: the last change to a delivered order is its delivery.
        var orderIds = items.Where(i => i.FoodOrderId != null).Select(i => i.FoodOrderId!.Value).ToList();
        var deliveredAt = await db.FoodOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.UpdatedAt, cancellationToken);

        var views = items
            .Select(i =>
            {
                var share = i.GrossAmount - i.CommissionAmount;
                var partnerAmount = i.Kind == SettlementItemKind.Sale ? share : -share;
                if (i.FoodOrderId is { } orderId)
                {
                    return new SettlementItemView(
                        null, orderId, "Food order", deliveredAt.GetValueOrDefault(orderId), i.Kind, i.GrossAmount, i.CommissionAmount, partnerAmount);
                }

                var booking = bookings[i.BookingId!.Value];
                return new SettlementItemView(
                    i.BookingId,
                    null,
                    titles.GetValueOrDefault(booking.ListingId, string.Empty),
                    booking.StartAt,
                    i.Kind,
                    i.GrossAmount,
                    i.CommissionAmount,
                    partnerAmount);
            })
            .OrderBy(v => v.ServiceStartAt)
            .ThenBy(v => v.BookingId ?? v.FoodOrderId)
            .ThenBy(v => v.Kind)
            .ToList();
        return new SettlementDetail(ToSummary(settlement, businessName), views);
    }

    private static SettlementSummary ToSummary(Settlement s, string businessName) => new(
        s.Id,
        s.BusinessId,
        businessName,
        s.PeriodStart,
        s.PeriodEnd,
        s.GrossAmount,
        s.CommissionAmount,
        s.RefundAmount,
        s.NetAmount,
        s.Status,
        s.PayoutReference,
        s.CreatedAt,
        s.ApprovedAt,
        s.PaidAt,
        s.Version);

    /// <summary>One statement line, for a booking or (with <c>BookingId</c> null) a food order.</summary>
    private sealed record Line(Guid? BookingId, Guid? FoodOrderId, SettlementItemKind Kind, decimal GrossAmount, decimal CommissionAmount);

    private sealed record Lines(IReadOnlyList<Line> Items, AppError? Error);

    /// <summary>
    /// The statement lines for everything finished before <paramref name="cutoff"/> and not yet on a statement, worked out from the
    /// ledger. For each booking the arithmetic of the statement (sale, less commission, less refunds) must equal what the ledger says
    /// the partner is owed for it, otherwise nothing is drafted.
    /// </summary>
    private async Task<Lines> BuildLinesAsync(Guid businessId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var bookingLines = await BuildBookingLinesAsync(businessId, cutoff, cancellationToken);
        if (bookingLines.Error is not null)
        {
            return bookingLines;
        }

        var foodLines = await BuildFoodLinesAsync(businessId, cutoff, cancellationToken);
        return foodLines.Error is not null ? foodLines : new Lines([.. bookingLines.Items, .. foodLines.Items], null);
    }

    /// <summary>
    /// Delivered food orders not yet on a statement. The restaurant's side of each is its dishes and packaging (less any discount); the
    /// commission is what the order's payment booked; together they must equal what the ledger says the restaurant is owed for it.
    /// </summary>
    private async Task<Lines> BuildFoodLinesAsync(Guid businessId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var onASale = db.SettlementItems
            .Where(i => i.Kind == SettlementItemKind.Sale && i.FoodOrderId != null)
            .Select(i => i.FoodOrderId!.Value);
        var delivered = await db.FoodOrders.AsNoTracking()
            .Where(o => o.BusinessId == businessId
                && o.Status == FoodOrderStatus.Delivered
                && o.UpdatedAt < cutoff
                && !onASale.Contains(o.Id))
            .OrderBy(o => o.UpdatedAt)
            .Select(o => new { o.Id, RestaurantGross = o.Subtotal - o.DiscountAmount + o.PackagingFee })
            .Take(MaxBookingsPerStatement)
            .ToListAsync(cancellationToken);
        if (delivered.Count == 0)
        {
            return new Lines([], null);
        }

        var orderIds = delivered.Select(o => o.Id).ToList();
        var partnerAccount = LedgerAccounts.PartnerPayable(businessId);
        var attempts = await db.PaymentAttempts.AsNoTracking()
            .Where(a => a.FoodOrderId != null && orderIds.Contains(a.FoodOrderId.Value) && a.Status == PaymentAttemptStatus.Captured)
            .Select(a => new { a.Id, OrderId = a.FoodOrderId!.Value })
            .ToListAsync(cancellationToken);
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.FoodOrderId != null && orderIds.Contains(e.FoodOrderId.Value)
                && (e.Account == partnerAccount || e.Account == LedgerAccounts.PlatformCommission))
            .Select(e => new { OrderId = e.FoodOrderId!.Value, e.TransactionId, e.Account, e.Amount })
            .ToListAsync(cancellationToken);

        var lines = new List<Line>();
        foreach (var order in delivered)
        {
            var mine = entries.Where(e => e.OrderId == order.Id).ToList();

            // The payment that paid the restaurant is the one whose capture posted a restaurant share; a second payment was held for
            // the customer instead and is no part of the statement.
            var paying = attempts
                .Where(a => a.OrderId == order.Id)
                .Select(a => LedgerService.TransactionId(LedgerService.CaptureKey(a.Id)))
                .Where(t => mine.Any(e => e.TransactionId == t && e.Account == partnerAccount))
                .ToHashSet();
            var partnerOwed = -mine.Where(e => e.Account == partnerAccount).Sum(e => e.Amount);
            if (paying.Count == 0 && partnerOwed == 0m)
            {
                continue; // not paid online (payments were off): nothing passed through OnePondy for it
            }

            var commissionKept = -mine.Where(e => e.Account == LedgerAccounts.PlatformCommission && paying.Contains(e.TransactionId)).Sum(e => e.Amount);
            if (paying.Count != 1 || order.RestaurantGross - commissionKept != partnerOwed)
            {
                return new Lines([], SettlementErrors.LedgerMismatch);
            }

            lines.Add(new Line(null, order.Id, SettlementItemKind.Sale, order.RestaurantGross, commissionKept));
        }

        return new Lines(lines, null);
    }

    private async Task<Lines> BuildBookingLinesAsync(Guid businessId, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var onASale = db.SettlementItems.Where(i => i.Kind == SettlementItemKind.Sale && i.BookingId != null).Select(i => i.BookingId!.Value);
        var onARefund = db.SettlementItems.Where(i => i.Kind == SettlementItemKind.Refund && i.BookingId != null).Select(i => i.BookingId!.Value);

        var finished = await db.Bookings.AsNoTracking()
            .Where(b => b.BusinessId == businessId
                && (b.Status == BookingStatus.Completed || b.Status == BookingStatus.Refunded)
                && b.UpdatedAt < cutoff
                && !onASale.Contains(b.Id))
            .OrderBy(b => b.UpdatedAt)
            .Select(b => b.Id)
            .Take(MaxBookingsPerStatement)
            .ToListAsync(cancellationToken);

        // Refunds that came after their sale was already on a statement.
        var businessBookings = db.Bookings.Where(b => b.BusinessId == businessId).Select(b => b.Id);
        var adjustments = await db.Refunds.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Processed
                && r.Reason != RefundReason.LatePayment
                && r.ProcessedAt < cutoff
                && r.BookingId != null
                && businessBookings.Contains(r.BookingId.Value)
                && onASale.Contains(r.BookingId.Value)
                && !onARefund.Contains(r.BookingId.Value))
            .Select(r => r.BookingId!.Value)
            .Distinct()
            .Take(MaxBookingsPerStatement)
            .ToListAsync(cancellationToken);

        var bookingIds = finished.Concat(adjustments).Distinct().ToList();
        if (bookingIds.Count == 0)
        {
            return new Lines([], null);
        }

        var partnerAccount = LedgerAccounts.PartnerPayable(businessId);
        var attempts = await db.PaymentAttempts.AsNoTracking()
            .Where(a => a.BookingId != null && bookingIds.Contains(a.BookingId.Value) && a.Status == PaymentAttemptStatus.Captured)
            .Select(a => new { a.Id, BookingId = a.BookingId!.Value, a.Amount })
            .ToListAsync(cancellationToken);
        var refunds = await db.Refunds.AsNoTracking()
            .Where(r => r.BookingId != null && bookingIds.Contains(r.BookingId.Value) && r.Status == RefundStatus.Processed && r.Reason != RefundReason.LatePayment)
            .Select(r => new { r.Id, BookingId = r.BookingId!.Value, r.Amount })
            .ToListAsync(cancellationToken);
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.BookingId != null && bookingIds.Contains(e.BookingId.Value)
                && (e.Account == partnerAccount || e.Account == LedgerAccounts.PlatformCommission))
            .Select(e => new { BookingId = e.BookingId!.Value, e.TransactionId, e.Account, e.Amount })
            .ToListAsync(cancellationToken);

        var lines = new List<Line>();
        foreach (var bookingId in bookingIds)
        {
            var mine = entries.Where(e => e.BookingId == bookingId).ToList();

            // Only the payment that paid the partner is a sale. A second payment for the same booking was held for the customer and
            // refunded (its capture posted nothing to the partner or the commission), so counting it would make the statement
            // disagree with the ledger for good.
            var captured = attempts
                .Where(a => a.BookingId == bookingId)
                .Where(a => mine.Any(e => e.Account == partnerAccount && e.TransactionId == LedgerService.TransactionId(LedgerService.CaptureKey(a.Id))))
                .ToList();
            var sold = captured.Sum(a => a.Amount);
            var returned = refunds.Where(r => r.BookingId == bookingId).Sum(r => r.Amount);
            var captureTransactions = captured.Select(a => LedgerService.TransactionId(LedgerService.CaptureKey(a.Id))).ToHashSet();
            var refundTransactions = refunds.Where(r => r.BookingId == bookingId).Select(r => LedgerService.TransactionId(LedgerService.RefundKey(r.Id))).ToHashSet();
            var commissionKept = -mine.Where(e => e.Account == LedgerAccounts.PlatformCommission && captureTransactions.Contains(e.TransactionId)).Sum(e => e.Amount);
            var commissionBack = mine.Where(e => e.Account == LedgerAccounts.PlatformCommission && refundTransactions.Contains(e.TransactionId)).Sum(e => e.Amount);
            var partnerOwed = -mine.Where(e => e.Account == partnerAccount).Sum(e => e.Amount);

            if (finished.Contains(bookingId))
            {
                if (partnerOwed <= 0m)
                {
                    continue; // free, or refunded in full: nothing is owed to the partner for it
                }

                if (sold - commissionKept - (returned - commissionBack) != partnerOwed)
                {
                    return new Lines([], SettlementErrors.LedgerMismatch);
                }

                lines.Add(new Line(bookingId, null, SettlementItemKind.Sale, sold, commissionKept));
            }

            if (returned > 0m && (finished.Contains(bookingId) || adjustments.Contains(bookingId)))
            {
                lines.Add(new Line(bookingId, null, SettlementItemKind.Refund, returned, commissionBack));
            }
        }

        return new Lines(lines, null);
    }
}

/// <summary>A downloadable statement.</summary>
public sealed record StatementFile(string FileName, string Content);

/// <summary>The partner's statement as CSV. Text that a spreadsheet would read as a formula is defused, since listing titles are typed by partners.</summary>
public static class StatementCsv
{
    private const string NewLine = "\r\n";

    public static string Render(SettlementDetail detail)
    {
        var s = detail.Settlement;
        var csv = new StringBuilder();
        Row(csv, "Business", Text(s.BusinessName));
        Row(csv, "Period", $"{s.PeriodStart:yyyy-MM-dd} to {s.PeriodEnd:yyyy-MM-dd}");
        Row(csv, "Status", s.Status.ToString());
        Row(csv, "Gross sales", Money(s.GrossAmount));
        Row(csv, "Commission", Money(s.CommissionAmount));
        Row(csv, "Refunds", Money(s.RefundAmount));
        Row(csv, "Net payable", Money(s.NetAmount));
        Row(csv, "Payout reference", Text(s.PayoutReference ?? string.Empty));
        csv.Append(NewLine);
        Row(csv, "Reference", "Item", "Service date", "Type", "Amount", "Commission", "Partner amount");
        foreach (var item in detail.Items)
        {
            Row(
                csv,
                (item.BookingId ?? item.FoodOrderId)?.ToString() ?? string.Empty,
                Text(item.ListingTitle),
                item.ServiceStartAt?.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                item.Kind == SettlementItemKind.Sale ? "Sale" : "Refund",
                Money(item.GrossAmount),
                Money(item.CommissionAmount),
                Money(item.PartnerAmount));
        }

        return csv.ToString();
    }

    /// <summary>Numbers are written as they are, including a leading minus on a refund.</summary>
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Text a person typed: one that starts like a formula gets an apostrophe so a spreadsheet shows it as text.</summary>
    internal static string Text(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static void Row(StringBuilder csv, params string[] fields) =>
        csv.AppendJoin(',', fields.Select(Quote)).Append(NewLine);

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
