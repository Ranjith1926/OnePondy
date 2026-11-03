namespace OnePondy.Domain.Support;

/// <summary>
/// Where a support request stands. <see cref="Open"/> means it is waiting for OnePondy; <see cref="WaitingForRequester"/> means support has
/// answered and the person is expected to reply; <see cref="Resolved"/> means support has closed it (the person can reopen it by replying,
/// for a limited time).
/// </summary>
public enum SupportTicketStatus
{
    Open = 1,
    WaitingForRequester = 2,
    Resolved = 3,
}

/// <summary>What a request is about, so it reaches the right person and can be counted. The person chooses; support can read the rest from the words.</summary>
public enum SupportTopic
{
    Booking = 1,
    FoodOrder = 2,
    Payment = 3,
    Account = 4,
    Business = 5,
    Other = 6,
}

public enum SupportAuthorKind
{
    Requester = 1,
    Staff = 2,
}

/// <summary>
/// A request for help from a customer, or from a business owner about their business (spec Phase 10, step 10.3). It is a conversation: the
/// person writes, support answers, and either can add to it until support resolves it. A ticket can point at one booking or one food order,
/// so support sees what it is about without being given the person's other history. Escalation (by an agent, or by the Worker when a time
/// target is missed) brings it to an administrator's attention; it does not change what anyone may do.
/// </summary>
public sealed class SupportTicket
{
    public const int MaxSubjectLength = 120;

    public const int MaxBodyLength = 2000;

    public const int MaxEscalationReasonLength = 200;

    private readonly List<SupportMessage> _messages = [];

    private SupportTicket()
    {
    }

    public Guid Id { get; private set; }

    public Guid RequesterUserId { get; private set; }

    /// <summary>Set when a business owner raised it about their business; null for a customer's own request.</summary>
    public Guid? BusinessId { get; private set; }

    public SupportTopic Topic { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public SupportTicketStatus Status { get; private set; }

    /// <summary>The booking or the order the request is about, if the person chose one (never both). It is theirs or their business's.</summary>
    public Guid? BookingId { get; private set; }

    public Guid? FoodOrderId { get; private set; }

    /// <summary>The staff member working on it, so two agents do not answer the same request; any support agent or administrator may still act on it.</summary>
    public Guid? AssignedToUserId { get; private set; }

    public DateTimeOffset? AssignedAt { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>Why it was escalated: a code for a missed target (<see cref="Escalations"/>), or the agent's own words.</summary>
    public string? EscalationReason { get; private set; }

    /// <summary>When support first answered; the first-reply target is measured against it.</summary>
    public DateTimeOffset? FirstResponseAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public DateTimeOffset LastRequesterMessageAt { get; private set; }

    public DateTimeOffset? LastStaffMessageAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<SupportMessage> Messages => _messages;

    public bool IsEscalated => EscalatedAt is not null;

    /// <summary>What a person sees as the reference: the random end of the id (the start of a time-ordered id is the same for tickets made minutes apart).</summary>
    public string Reference => Id.ToString("N")[^8..].ToUpperInvariant();

    public static SupportTicket Open(
        Guid requesterUserId,
        Guid? businessId,
        SupportTopic topic,
        string subject,
        string firstMessage,
        Guid? bookingId,
        Guid? foodOrderId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (subject.Length > MaxSubjectLength)
        {
            throw new ArgumentException($"A subject is at most {MaxSubjectLength} characters.", nameof(subject));
        }

        if (bookingId is not null && foodOrderId is not null)
        {
            throw new ArgumentException("A request is about a booking or an order, not both.", nameof(foodOrderId));
        }

        var ticket = new SupportTicket
        {
            Id = Guid.CreateVersion7(),
            RequesterUserId = requesterUserId,
            BusinessId = businessId,
            Topic = topic,
            Subject = subject,
            Status = SupportTicketStatus.Open,
            BookingId = bookingId,
            FoodOrderId = foodOrderId,
            LastRequesterMessageAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ticket.AddMessage(requesterUserId, SupportAuthorKind.Requester, firstMessage, now);
        return ticket;
    }

    /// <summary>
    /// The person writes again. It puts the ticket back in support's queue. A resolved ticket reopens only within <paramref name="reopenWindow"/>
    /// of being resolved: after that, a new request keeps old and new problems apart.
    /// </summary>
    public void AddRequesterMessage(Guid requesterUserId, string body, DateTimeOffset now, TimeSpan reopenWindow)
    {
        if (Status == SupportTicketStatus.Resolved)
        {
            if (now - ResolvedAt!.Value > reopenWindow)
            {
                throw new InvalidOperationException("This request was resolved too long ago to reopen.");
            }

            Status = SupportTicketStatus.Open;
            ResolvedAt = null;
            ResolvedByUserId = null;
        }
        else
        {
            Status = SupportTicketStatus.Open;
        }

        AddMessage(requesterUserId, SupportAuthorKind.Requester, body, now);
        LastRequesterMessageAt = now;
        UpdatedAt = now;
    }

    /// <summary>Support answers. The ticket now waits for the person, and the first answer stops the first-reply clock. An unassigned ticket goes to whoever answered.</summary>
    public void AddStaffMessage(Guid staffUserId, string body, DateTimeOffset now)
    {
        RequireNotResolved();
        AddMessage(staffUserId, SupportAuthorKind.Staff, body, now);
        Status = SupportTicketStatus.WaitingForRequester;
        FirstResponseAt ??= now;
        LastStaffMessageAt = now;
        if (AssignedToUserId is null)
        {
            AssignedToUserId = staffUserId;
            AssignedAt = now;
        }

        UpdatedAt = now;
    }

    public void Assign(Guid staffUserId, DateTimeOffset now)
    {
        RequireNotResolved();
        AssignedToUserId = staffUserId;
        AssignedAt = now;
        UpdatedAt = now;
    }

    public void Release(DateTimeOffset now)
    {
        RequireNotResolved();
        AssignedToUserId = null;
        AssignedAt = null;
        UpdatedAt = now;
    }

    /// <summary>Brings the ticket to an administrator's attention, once. Returns false (and changes nothing) if it was already escalated.</summary>
    public bool Escalate(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > MaxEscalationReasonLength)
        {
            throw new ArgumentException($"A reason is at most {MaxEscalationReasonLength} characters.", nameof(reason));
        }

        RequireNotResolved();
        if (IsEscalated)
        {
            return false;
        }

        EscalatedAt = now;
        EscalationReason = reason;
        UpdatedAt = now;
        return true;
    }

    public void Resolve(Guid staffUserId, DateTimeOffset now)
    {
        RequireNotResolved();
        Status = SupportTicketStatus.Resolved;
        ResolvedAt = now;
        ResolvedByUserId = staffUserId;
        UpdatedAt = now;
    }

    private void AddMessage(Guid authorUserId, SupportAuthorKind kind, string body, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (body.Length > MaxBodyLength)
        {
            throw new ArgumentException($"A message is at most {MaxBodyLength} characters.", nameof(body));
        }

        _messages.Add(new SupportMessage(Guid.CreateVersion7(), Id, authorUserId, kind, body, now));
    }

    private void RequireNotResolved()
    {
        if (Status == SupportTicketStatus.Resolved)
        {
            throw new InvalidOperationException("This request is resolved.");
        }
    }

    /// <summary>The codes recorded when the Worker escalates a ticket that missed a time target.</summary>
    public static class Escalations
    {
        public const string FirstReplyOverdue = "first_reply_overdue";

        public const string ResolutionOverdue = "resolution_overdue";
    }
}

/// <summary>One message in a request: written by the person or by support.</summary>
public sealed class SupportMessage
{
    private SupportMessage()
    {
    }

    internal SupportMessage(Guid id, Guid ticketId, Guid authorUserId, SupportAuthorKind authorKind, string body, DateTimeOffset createdAt)
    {
        Id = id;
        TicketId = ticketId;
        AuthorUserId = authorUserId;
        AuthorKind = authorKind;
        Body = body;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TicketId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public SupportAuthorKind AuthorKind { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}
