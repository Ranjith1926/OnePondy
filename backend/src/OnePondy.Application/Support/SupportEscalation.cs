using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Support;

namespace OnePondy.Application.Support;

/// <summary>
/// The Worker's check that nothing waits unseen (spec Phase 10, step 10.3, decision R9): a request that has had no first answer within the
/// first-reply target, or is still unresolved after the resolution target, is escalated once, with a code saying which. The agents' list
/// already flags it; this also records it, in the audit log, so an administrator can see when and why.
/// </summary>
public sealed class SupportEscalation(IOnePondyDbContext db, AuditTrail audit, IOptions<SupportOptions> options, TimeProvider time)
{
    /// <summary>The most escalated in one run, so a long outage does not become one huge save; the next run picks up the rest.</summary>
    private const int BatchSize = 100;

    public async Task<int> EscalateOverdueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var firstReplyCutoff = now - options.Value.FirstReplyTarget;
        var resolutionCutoff = now - options.Value.ResolutionTarget;
        var due = await db.SupportTickets
            .Where(t => t.Status != SupportTicketStatus.Resolved
                && t.EscalatedAt == null
                && ((t.FirstResponseAt == null && t.CreatedAt <= firstReplyCutoff) || t.CreatedAt <= resolutionCutoff))
            .OrderBy(t => t.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var ticket in due)
        {
            var reason = ticket.CreatedAt <= resolutionCutoff
                ? SupportTicket.Escalations.ResolutionOverdue
                : SupportTicket.Escalations.FirstReplyOverdue;
            ticket.Escalate(reason, now);
            audit.Record(null, AuditActions.SupportTicketEscalated, AuditEntityTypes.SupportTicket, ticket.Id, new { reason });
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }
}
