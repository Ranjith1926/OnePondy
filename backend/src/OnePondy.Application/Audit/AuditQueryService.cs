using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Application.Places;
using OnePondy.Application.Staff;

namespace OnePondy.Application.Audit;

public sealed record AuditEntryView(
    Guid Id,
    DateTimeOffset OccurredAt,
    UserRef? Actor,
    string Action,
    string EntityType,
    string EntityId,
    string? Details);

public sealed record AuditQuery(string? EntityType, string? EntityId, string? Action, Guid? ActorUserId, int? Page, int? PageSize);

public sealed class AuditQueryService(IOnePondyDbContext db, IStaffDirectory staff)
{
    public async Task<PagedResult<AuditEntryView>> ListAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var entries = db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            entries = entries.Where(e => e.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            entries = entries.Where(e => e.EntityId == query.EntityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(e => e.Action.StartsWith(query.Action));
        }

        if (query.ActorUserId is { } actor)
        {
            entries = entries.Where(e => e.ActorUserId == actor);
        }

        var total = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var actors = items.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value).Distinct().ToList();
        var names = await staff.GetNamesAsync(actors, cancellationToken);
        return new PagedResult<AuditEntryView>(
            [.. items.Select(e => new AuditEntryView(
                e.Id,
                e.OccurredAt,
                e.ActorUserId is { } id ? new UserRef(id, names.GetValueOrDefault(id, "Unknown user")) : null,
                e.Action,
                e.EntityType,
                e.EntityId,
                e.Details))],
            page,
            pageSize,
            total);
    }
}
