using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Support;

namespace OnePondy.Application.Support;

/// <summary>
/// Support requests (spec Phase 10, step 10.3). A customer, or a business owner about their business, writes to support and gets answers in a
/// conversation; support agents work a queue in the console. The service keeps three promises: a person sees only their own requests; an agent
/// sees the request and the one booking or order it is about, never the person's other history, and every time an agent opens a request or asks
/// for the person's contact details it is audited; and nothing waits unseen, because a request that misses its time target is flagged and escalated.
/// </summary>
public sealed class SupportService(
    IOnePondyDbContext db,
    AuditTrail audit,
    IUserAccountStore accounts,
    IOptions<SupportOptions> options,
    TimeProvider time)
{
    private const string AnonymousCustomer = "A customer";

    private const string AnonymousOwner = "A business owner";

    private static readonly JsonNamingPolicy Camel = JsonNamingPolicy.CamelCase;

    // --- The person asking for help ---

    public async Task<Result<TicketDetail>> CreateAsync(Guid userId, NewTicketInput input, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var subject = errors.Required("subject", input.Subject, SupportTicket.MaxSubjectLength);
        var message = errors.Required("message", input.Message, SupportTicket.MaxBodyLength);
        if (!SupportTopics.TryParse(input.Topic, out var topic))
        {
            errors.Add("topic", "Choose what this is about.");
        }

        if (input.BookingId is not null && input.FoodOrderId is not null)
        {
            errors.Add("bookingId", "A request can be about a booking or an order, not both.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        if (!await db.ModuleFlags.AsNoTracking().AnyAsync(f => f.Key == ModuleKeys.Support && f.Status == ModuleStatus.Available, cancellationToken))
        {
            return SupportErrors.NotAvailable;
        }

        var settings = options.Value;
        var open = await db.SupportTickets.AsNoTracking()
            .CountAsync(t => t.RequesterUserId == userId && t.Status != SupportTicketStatus.Resolved, cancellationToken);
        if (open >= settings.MaxOpenTicketsPerUser)
        {
            return SupportErrors.TooManyOpen;
        }

        if (input.BusinessId is { } businessId
            && !await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId && b.OwnerUserId == userId, cancellationToken))
        {
            // Someone else's business is answered as "not found", as everywhere else, so it is not revealed.
            return SupportErrors.NotFound;
        }

        if (!await LinkedSubjectBelongsAsync(userId, input.BusinessId, input.BookingId, input.FoodOrderId, cancellationToken))
        {
            return SupportErrors.LinkedNotFound;
        }

        var ticket = SupportTicket.Open(userId, input.BusinessId, topic, subject, message, input.BookingId, input.FoodOrderId, time.GetUtcNow());
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken);
        return await ToRequesterDetailAsync(ticket, cancellationToken);
    }

    public async Task<IReadOnlyList<TicketSummary>> ListMineAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tickets = await db.SupportTickets.AsNoTracking()
            .Where(t => t.RequesterUserId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .ThenByDescending(t => t.Id)
            .ToListAsync(cancellationToken);
        return [.. tickets.Select(t => new TicketSummary(t.Id, t.Reference, t.Topic, t.Subject, t.Status, t.CreatedAt, t.UpdatedAt))];
    }

    public async Task<Result<TicketDetail>> GetMineAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var ticket = await db.SupportTickets.AsNoTracking().Include(t => t.Messages)
            .SingleOrDefaultAsync(t => t.Id == id && t.RequesterUserId == userId, cancellationToken);
        return ticket is null ? SupportErrors.NotFound : await ToRequesterDetailAsync(ticket, cancellationToken);
    }

    /// <summary>The person adds to a request. It goes back in support's queue; a resolved one reopens if it was resolved recently enough.</summary>
    public async Task<Result<TicketDetail>> ReplyAsync(Guid id, Guid userId, MessageInput input, CancellationToken cancellationToken)
    {
        var ticket = await db.SupportTickets.Include(t => t.Messages)
            .SingleOrDefaultAsync(t => t.Id == id && t.RequesterUserId == userId, cancellationToken);
        if (ticket is null)
        {
            return SupportErrors.NotFound;
        }

        var errors = new FieldErrors();
        var body = errors.Required("body", input.Body, SupportTicket.MaxBodyLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        if (ticket.Messages.Count >= options.Value.MaxMessagesPerTicket)
        {
            return SupportErrors.TooManyMessages;
        }

        var before = ticket.Messages.Count;
        try
        {
            ticket.AddRequesterMessage(userId, body, time.GetUtcNow(), options.Value.ReopenWindow);
        }
        catch (InvalidOperationException)
        {
            return SupportErrors.CannotReopen;
        }

        // A message has its own key already, so EF would take it for an existing row; say it is new.
        foreach (var message in ticket.Messages.Skip(before))
        {
            db.MarkAdded(message);
        }

        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await ToRequesterDetailAsync(ticket, cancellationToken);
    }

    // --- Support agents ---

    public async Task<Result<PagedResult<AgentTicketSummary>>> AgentListAsync(
        AgentTicketQuery query, Guid staffUserId, CancellationToken cancellationToken)
    {
        var (p, size) = Paging.Normalize(query.Page, query.PageSize);
        var now = time.GetUtcNow();
        var view = query.View ?? "needs_reply";
        if (ApplyView(db.SupportTickets.AsNoTracking(), view, staffUserId, now) is not { } tickets)
        {
            return SupportErrors.BadView();
        }

        var total = await tickets.CountAsync(cancellationToken);
        var ordered = view == "resolved"
            ? tickets.OrderByDescending(t => t.ResolvedAt).ThenByDescending(t => t.Id)
            : tickets.OrderByDescending(t => t.EscalatedAt != null).ThenBy(t => t.LastRequesterMessageAt).ThenBy(t => t.Id);
        var page = await ordered.Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new PagedResult<AgentTicketSummary>(await ToAgentSummariesAsync(page, now, cancellationToken), p, size, total);
    }

    public async Task<SupportCounts> CountsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var open = db.SupportTickets.AsNoTracking().Where(t => t.Status != SupportTicketStatus.Resolved);
        return new SupportCounts(
            await open.CountAsync(t => t.Status == SupportTicketStatus.Open, cancellationToken),
            await open.CountAsync(t => t.Status == SupportTicketStatus.WaitingForRequester, cancellationToken),
            await open.CountAsync(t => t.AssignedToUserId == null, cancellationToken),
            await ApplyView(db.SupportTickets.AsNoTracking(), "overdue", Guid.Empty, now)!.CountAsync(cancellationToken),
            await open.CountAsync(t => t.EscalatedAt != null, cancellationToken));
    }

    /// <summary>Opens a request for an agent. Each time is audited: it is the person's words an agent is reading.</summary>
    public async Task<Result<AgentTicketDetail>> AgentGetAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken)
    {
        var ticket = await db.SupportTickets.AsNoTracking().Include(t => t.Messages)
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return SupportErrors.NotFound;
        }

        audit.Record(staffUserId, AuditActions.SupportTicketViewed, AuditEntityTypes.SupportTicket, ticket.Id);
        await db.SaveChangesAsync(cancellationToken);
        return await ToAgentDetailAsync(ticket, cancellationToken);
    }

    public async Task<Result<AgentTicketDetail>> AgentReplyAsync(
        Guid id, AgentMessageInput input, Guid staffUserId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var body = errors.Required("body", input.Body, SupportTicket.MaxBodyLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        return await ChangeAsync(id, input.Version, cancellationToken, ticket =>
        {
            if (ticket.Status == SupportTicketStatus.Resolved)
            {
                return SupportErrors.Resolved;
            }

            if (ticket.Messages.Count >= options.Value.MaxMessagesPerTicket)
            {
                return SupportErrors.TooManyMessages;
            }

            var before = ticket.Messages.Count;
            ticket.AddStaffMessage(staffUserId, body, time.GetUtcNow());
            foreach (var message in ticket.Messages.Skip(before))
            {
                db.MarkAdded(message);
            }

            return null;
        });
    }

    /// <summary>An agent takes a request, so two agents do not answer the same one. It can be taken over from another agent.</summary>
    public Task<Result<AgentTicketDetail>> AssignAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        ChangeAsync(id, version, cancellationToken, ticket =>
        {
            if (ticket.Status == SupportTicketStatus.Resolved)
            {
                return SupportErrors.Resolved;
            }

            var previous = ticket.AssignedToUserId;
            ticket.Assign(staffUserId, time.GetUtcNow());
            audit.Record(staffUserId, AuditActions.SupportTicketAssigned, AuditEntityTypes.SupportTicket, ticket.Id, new { previous });
            return null;
        });

    public Task<Result<AgentTicketDetail>> ReleaseAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        ChangeAsync(id, version, cancellationToken, ticket =>
        {
            if (ticket.Status == SupportTicketStatus.Resolved)
            {
                return SupportErrors.Resolved;
            }

            ticket.Release(time.GetUtcNow());
            audit.Record(staffUserId, AuditActions.SupportTicketReleased, AuditEntityTypes.SupportTicket, ticket.Id);
            return null;
        });

    public Task<Result<AgentTicketDetail>> ResolveAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        ChangeAsync(id, version, cancellationToken, ticket =>
        {
            if (ticket.Status == SupportTicketStatus.Resolved)
            {
                return SupportErrors.Resolved;
            }

            ticket.Resolve(staffUserId, time.GetUtcNow());
            audit.Record(staffUserId, AuditActions.SupportTicketResolved, AuditEntityTypes.SupportTicket, ticket.Id);
            return null;
        });

    /// <summary>An agent brings a request to an administrator's attention, with the reason. The Worker does the same, with a code, for a missed target.</summary>
    public async Task<Result<AgentTicketDetail>> EscalateAsync(Guid id, EscalateInput input, Guid staffUserId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var reason = errors.Required("reason", input.Reason, SupportTicket.MaxEscalationReasonLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        return await ChangeAsync(id, input.Version, cancellationToken, ticket =>
        {
            if (ticket.Status == SupportTicketStatus.Resolved)
            {
                return SupportErrors.Resolved;
            }

            if (!ticket.Escalate(reason, time.GetUtcNow()))
            {
                return SupportErrors.AlreadyEscalated;
            }

            audit.Record(staffUserId, AuditActions.SupportTicketEscalated, AuditEntityTypes.SupportTicket, ticket.Id, new { reason });
            return null;
        });
    }

    /// <summary>
    /// How to reach the person. An agent does not get this by opening a request (the thread is where support talks to them); asking for it is
    /// a separate, audited step, for the rare request that needs it.
    /// </summary>
    public async Task<Result<ContactView>> ContactAsync(Guid id, Guid staffUserId, CancellationToken cancellationToken)
    {
        var ticket = await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return SupportErrors.NotFound;
        }

        var profile = await accounts.GetProfileAsync(ticket.RequesterUserId, cancellationToken);
        audit.Record(staffUserId, AuditActions.SupportContactViewed, AuditEntityTypes.SupportTicket, ticket.Id);
        await db.SaveChangesAsync(cancellationToken);
        return new ContactView(profile?.PhoneNumber, profile?.Email);
    }

    // --- Everyone ---

    public GrievanceOfficerView Grievance()
    {
        var officer = options.Value.GrievanceOfficer;
        var configured = !string.IsNullOrWhiteSpace(officer.Name) && !string.IsNullOrWhiteSpace(officer.Email);
        return new GrievanceOfficerView(
            configured,
            configured ? officer.Name : null,
            configured ? officer.Email : null,
            configured ? officer.Phone : null,
            configured ? officer.Address : null,
            officer.AcknowledgeWithinHours,
            officer.ResolveWithinDays);
    }

    // --- Shared ---

    /// <summary>The queue an agent asked for. Open tickets only, unless the list is about resolved ones or all of them.</summary>
    private IQueryable<SupportTicket>? ApplyView(IQueryable<SupportTicket> tickets, string view, Guid staffUserId, DateTimeOffset now)
    {
        var settings = options.Value;
        var firstReplyCutoff = now - settings.FirstReplyTarget;
        var resolutionCutoff = now - settings.ResolutionTarget;
        return view switch
        {
            "needs_reply" => tickets.Where(t => t.Status == SupportTicketStatus.Open),
            "waiting" => tickets.Where(t => t.Status == SupportTicketStatus.WaitingForRequester),
            "mine" => tickets.Where(t => t.Status != SupportTicketStatus.Resolved && t.AssignedToUserId == staffUserId),
            "unassigned" => tickets.Where(t => t.Status != SupportTicketStatus.Resolved && t.AssignedToUserId == null),
            "escalated" => tickets.Where(t => t.Status != SupportTicketStatus.Resolved && t.EscalatedAt != null),
            "overdue" => tickets.Where(t => t.Status != SupportTicketStatus.Resolved
                && ((t.FirstResponseAt == null && t.CreatedAt <= firstReplyCutoff) || t.CreatedAt <= resolutionCutoff)),
            "resolved" => tickets.Where(t => t.Status == SupportTicketStatus.Resolved),
            "all" => tickets,
            _ => null,
        };
    }

    private async Task<Result<AgentTicketDetail>> ChangeAsync(
        Guid id, uint version, CancellationToken cancellationToken, Func<SupportTicket, AppError?> change)
    {
        var ticket = await db.SupportTickets.Include(t => t.Messages).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return SupportErrors.NotFound;
        }

        db.ExpectVersion(ticket, version);
        if (change(ticket) is { } refused)
        {
            return refused;
        }

        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await ToAgentDetailAsync(ticket, cancellationToken);
    }

    /// <summary>Saves, turning a lost race with another writer into "changed" instead of a crash.</summary>
    private async Task<AppError?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            db.ClearTracked();
            return SupportErrors.Changed;
        }
    }

    /// <summary>The booking or order must be the person's own; for a business owner, it must be their business's.</summary>
    private async Task<bool> LinkedSubjectBelongsAsync(
        Guid userId, Guid? businessId, Guid? bookingId, Guid? foodOrderId, CancellationToken cancellationToken)
    {
        if (bookingId is { } booking)
        {
            return await db.Bookings.AsNoTracking().AnyAsync(
                b => b.Id == booking && (businessId == null ? b.CustomerUserId == userId : b.BusinessId == businessId),
                cancellationToken);
        }

        if (foodOrderId is { } order)
        {
            return await db.FoodOrders.AsNoTracking().AnyAsync(
                o => o.Id == order && (businessId == null ? o.CustomerUserId == userId : o.BusinessId == businessId),
                cancellationToken);
        }

        return true;
    }

    private async Task<LinkedSubjectView?> LinkedSubjectAsync(SupportTicket ticket, CancellationToken cancellationToken)
    {
        if (ticket.BookingId is { } bookingId)
        {
            var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
            if (booking is null)
            {
                return null;
            }

            var title = await db.Listings.AsNoTracking().Where(l => l.Id == booking.ListingId).Select(l => l.Title).SingleOrDefaultAsync(cancellationToken);
            return new LinkedSubjectView(
                "booking",
                booking.Id,
                title ?? string.Empty,
                Camel.ConvertName(booking.Status.ToString()),
                booking.TotalAmount,
                booking.Currency,
                booking.StartAt ?? booking.CreatedAt);
        }

        if (ticket.FoodOrderId is { } orderId)
        {
            var order = await db.FoodOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
            if (order is null)
            {
                return null;
            }

            var name = await db.Businesses.AsNoTracking().Where(b => b.Id == order.BusinessId).Select(b => b.Name).SingleOrDefaultAsync(cancellationToken);
            return new LinkedSubjectView(
                "food_order",
                order.Id,
                name ?? string.Empty,
                Camel.ConvertName(order.Status.ToString()),
                order.TotalAmount,
                order.Currency,
                order.CreatedAt);
        }

        return null;
    }

    private async Task<TicketDetail> ToRequesterDetailAsync(SupportTicket ticket, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var canReply = ticket.Status != SupportTicketStatus.Resolved || now - ticket.ResolvedAt!.Value <= options.Value.ReopenWindow;
        return new TicketDetail(
            ticket.Id,
            ticket.Reference,
            ticket.Topic,
            ticket.Subject,
            ticket.Status,
            await LinkedSubjectAsync(ticket, cancellationToken),
            [.. ticket.Messages.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Select(m => new TicketMessageView(m.Id, m.AuthorKind == SupportAuthorKind.Staff, m.Body, m.CreatedAt))],
            canReply,
            ticket.CreatedAt,
            ticket.UpdatedAt);
    }

    private async Task<AgentTicketDetail> ToAgentDetailAsync(SupportTicket ticket, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var summary = (await ToAgentSummariesAsync([ticket], now, cancellationToken)).Single();
        var names = new Dictionary<Guid, string>();
        var messages = new List<AgentMessageView>();
        foreach (var message in ticket.Messages.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id))
        {
            if (!names.TryGetValue(message.AuthorUserId, out var name))
            {
                name = await FirstNameAsync(message.AuthorUserId, message.AuthorKind == SupportAuthorKind.Staff ? "Support" : AnonymousCustomer, cancellationToken);
                names[message.AuthorUserId] = name;
            }

            messages.Add(new AgentMessageView(
                message.Id,
                message.AuthorKind == SupportAuthorKind.Staff ? "staff" : "requester",
                name,
                message.Body,
                message.CreatedAt));
        }

        return new AgentTicketDetail(
            summary,
            await LinkedSubjectAsync(ticket, cancellationToken),
            messages,
            ticket.ResolvedByUserId,
            ticket.ResolvedAt,
            ticket.Version);
    }

    private async Task<IReadOnlyList<AgentTicketSummary>> ToAgentSummariesAsync(
        IReadOnlyList<SupportTicket> tickets, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var businessIds = tickets.Where(t => t.BusinessId is not null).Select(t => t.BusinessId!.Value).Distinct().ToList();
        var businessNames = businessIds.Count == 0
            ? []
            : await db.Businesses.AsNoTracking().Where(b => businessIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        var rows = new List<AgentTicketSummary>();
        foreach (var t in tickets)
        {
            var partner = t.BusinessId is not null;
            var name = await FirstNameAsync(t.RequesterUserId, partner ? AnonymousOwner : AnonymousCustomer, cancellationToken);
            var unresolved = t.Status != SupportTicketStatus.Resolved;
            rows.Add(new AgentTicketSummary(
                t.Id,
                t.Reference,
                t.Topic,
                t.Subject,
                t.Status,
                partner ? "partner" : "customer",
                name,
                t.BusinessId is { } businessId ? businessNames.GetValueOrDefault(businessId) : null,
                t.AssignedToUserId,
                t.IsEscalated,
                t.EscalationReason,
                unresolved && t.FirstResponseAt is null && now - t.CreatedAt > settings.FirstReplyTarget,
                unresolved && now - t.CreatedAt > settings.ResolutionTarget,
                t.CreatedAt,
                t.LastRequesterMessageAt));
        }

        return rows;
    }

    /// <summary>The first name on the account, so an agent can address the person; never a phone number or an address, which a name that looks like one is replaced by.</summary>
    private async Task<string> FirstNameAsync(Guid userId, string fallback, CancellationToken cancellationToken)
    {
        var profile = await accounts.GetProfileAsync(userId, cancellationToken);
        var first = profile?.DisplayName?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) || first.Count(char.IsDigit) >= 4 || first.Contains('@') ? fallback : first;
    }
}
