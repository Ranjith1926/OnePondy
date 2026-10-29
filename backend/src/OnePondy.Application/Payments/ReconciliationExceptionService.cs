using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Payments;

public sealed record ReconciliationExceptionView(
    Guid Id,
    ReconciliationKind Kind,
    ReconciliationStatus Status,
    Guid? BookingId,
    Guid? PaymentAttemptId,
    Guid? RefundId,
    Guid? GatewayEventId,
    string? ProviderReference,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    string Detail,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int Occurrences,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote);

public static class ReconciliationErrors
{
    public static readonly AppError NotFound = new(
        "reconciliation_exception_not_found",
        ErrorKind.NotFound,
        "That reconciliation exception does not exist.");

    public static readonly AppError NotOpen = new(
        "reconciliation_exception_closed",
        ErrorKind.Conflict,
        "That exception has already been resolved.");
}

public sealed record ReconciliationQuery(ReconciliationStatus? Status, ReconciliationKind? Kind, int? Page, int? PageSize);

/// <summary>What the console shows of reconciliation: the differences found, and a way for an administrator to close one with a note.</summary>
public sealed class ReconciliationExceptionService(IOnePondyDbContext db, AuditTrail audit, TimeProvider time)
{
    public async Task<PagedResult<ReconciliationExceptionView>> ListAsync(ReconciliationQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var exceptions = db.ReconciliationExceptions.AsNoTracking();
        if (query.Status is { } status)
        {
            exceptions = exceptions.Where(e => e.Status == status);
        }

        if (query.Kind is { } kind)
        {
            exceptions = exceptions.Where(e => e.Kind == kind);
        }

        var total = await exceptions.CountAsync(cancellationToken);
        var items = await exceptions
            .OrderByDescending(e => e.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<ReconciliationExceptionView>([.. items.Select(ToView)], page, pageSize, total);
    }

    /// <summary>How many problems still need a person, for a badge or an alert.</summary>
    public Task<int> OpenCountAsync(CancellationToken cancellationToken) =>
        db.ReconciliationExceptions.AsNoTracking().CountAsync(e => e.Status == ReconciliationStatus.Open, cancellationToken);

    public async Task<Result<ReconciliationExceptionView>> ResolveAsync(Guid id, string? note, Guid staffUserId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("note", note, ReconciliationException.MaxNoteLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        var exception = await db.ReconciliationExceptions.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (exception is null)
        {
            return ReconciliationErrors.NotFound;
        }

        if (exception.Status != ReconciliationStatus.Open)
        {
            return ReconciliationErrors.NotOpen;
        }

        exception.Resolve(staffUserId, trimmed, time.GetUtcNow());
        audit.Record(
            staffUserId,
            AuditActions.ReconciliationExceptionResolved,
            AuditEntityTypes.ReconciliationException,
            exception.Id,
            new { kind = exception.Kind.ToString(), exception.BookingId, exception.ProviderReference, note = trimmed });
        await db.SaveChangesAsync(cancellationToken);
        return ToView(exception);
    }

    private static ReconciliationExceptionView ToView(ReconciliationException e) => new(
        e.Id,
        e.Kind,
        e.Status,
        e.BookingId,
        e.PaymentAttemptId,
        e.RefundId,
        e.GatewayEventId,
        e.ProviderReference,
        e.ExpectedAmount,
        e.ActualAmount,
        e.Detail,
        e.FirstSeenAt,
        e.LastSeenAt,
        e.Occurrences,
        e.ResolvedAt,
        e.ResolutionNote);
}
