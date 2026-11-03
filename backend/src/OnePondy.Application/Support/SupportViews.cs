using OnePondy.Application.Common;
using OnePondy.Domain.Support;

namespace OnePondy.Application.Support;

/// <summary>A new request. <paramref name="BusinessId"/> is set by a business owner writing about their business; <paramref name="Topic"/> is a topic name such as "booking" or "foodOrder".</summary>
public sealed record NewTicketInput(string? Topic, string? Subject, string? Message, Guid? BookingId, Guid? FoodOrderId, Guid? BusinessId);

public sealed record MessageInput(string? Body);

/// <summary>An agent's action on a ticket, carrying the version they were looking at.</summary>
public sealed record AgentVersionInput(uint Version);

public sealed record AgentMessageInput(uint Version, string? Body);

public sealed record EscalateInput(uint Version, string? Reason);

/// <summary>What the agents' list shows: <c>needs_reply</c> (default), <c>waiting</c>, <c>mine</c>, <c>unassigned</c>, <c>escalated</c>, <c>overdue</c>, <c>resolved</c> or <c>all</c>.</summary>
public sealed record AgentTicketQuery(string? View, int? Page, int? PageSize);

/// <summary>What a request is about, in a few lines: enough for support to see the thing, not the person's other history.</summary>
public sealed record LinkedSubjectView(
    string Kind,
    Guid Id,
    string Title,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset When);

/// <summary>A message as the person sees it. Support's messages carry no staff member's name.</summary>
public sealed record TicketMessageView(Guid Id, bool FromSupport, string Body, DateTimeOffset CreatedAt);

public sealed record TicketSummary(
    Guid Id,
    string Reference,
    SupportTopic Topic,
    string Subject,
    SupportTicketStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TicketDetail(
    Guid Id,
    string Reference,
    SupportTopic Topic,
    string Subject,
    SupportTicketStatus Status,
    LinkedSubjectView? About,
    IReadOnlyList<TicketMessageView> Messages,
    // True while the person can still add to it: not resolved, or resolved recently enough to reopen.
    bool CanReply,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A request in the agents' list: who and what, how old, who has it, and which targets it has missed.</summary>
public sealed record AgentTicketSummary(
    Guid Id,
    string Reference,
    SupportTopic Topic,
    string Subject,
    SupportTicketStatus Status,
    // "customer" or "partner".
    string RequesterKind,
    string RequesterName,
    string? BusinessName,
    Guid? AssignedToUserId,
    bool Escalated,
    string? EscalationReason,
    bool FirstReplyOverdue,
    bool ResolutionOverdue,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastRequesterMessageAt);

/// <summary>A message as an agent sees it: the author's first name, and whether it was the person or support.</summary>
public sealed record AgentMessageView(Guid Id, string Kind, string AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record AgentTicketDetail(
    AgentTicketSummary Ticket,
    LinkedSubjectView? About,
    IReadOnlyList<AgentMessageView> Messages,
    Guid? ResolvedByUserId,
    DateTimeOffset? ResolvedAt,
    uint Version);

public sealed record SupportCounts(int NeedsReply, int Waiting, int Unassigned, int Overdue, int Escalated);

/// <summary>How to reach the person, shown to an agent only on request, which is audited.</summary>
public sealed record ContactView(string? PhoneNumber, string? Email);

public sealed record GrievanceOfficerView(
    bool Configured,
    string? Name,
    string? Email,
    string? Phone,
    string? Address,
    int AcknowledgeWithinHours,
    int ResolveWithinDays);

public static class SupportErrors
{
    public static readonly AppError NotFound = new("ticket_not_found", ErrorKind.NotFound, "That request does not exist.");

    public static readonly AppError LinkedNotFound = new(
        "support_linked_not_found",
        ErrorKind.NotFound,
        "We couldn't find that booking or order.");

    public static readonly AppError NotAvailable = new(
        "support_unavailable",
        ErrorKind.Unavailable,
        "Support requests are not open yet.");

    public static readonly AppError TooManyOpen = new(
        "support_too_many_open",
        ErrorKind.Conflict,
        "You already have several open requests. Wait for an answer, or add to one of them.");

    public static readonly AppError TooManyMessages = new(
        "support_too_many_messages",
        ErrorKind.Conflict,
        "This request has a lot of messages already. Start a new request for anything else.");

    public static readonly AppError CannotReopen = new(
        "support_cannot_reopen",
        ErrorKind.Conflict,
        "This request was resolved a while ago. Please start a new one.");

    public static readonly AppError Resolved = new(
        "support_resolved",
        ErrorKind.Conflict,
        "This request is resolved.");

    public static readonly AppError AlreadyEscalated = new(
        "support_already_escalated",
        ErrorKind.Conflict,
        "This request has already been escalated.");

    public static readonly AppError Changed = new(
        "support_changed",
        ErrorKind.Conflict,
        "This request changed while you were looking at it. Reload and try again.");

    public static AppError BadView() => AppError.Validation(
        new Dictionary<string, string[]> { ["view"] = ["Unknown list."] });
}

/// <summary>Reads a topic name as the app and console send it: "foodOrder", "food_order" and "FoodOrder" are the same topic.</summary>
public static class SupportTopics
{
    public static bool TryParse(string? value, out SupportTopic topic)
    {
        topic = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var wanted = value.Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<SupportTopic>())
        {
            if (string.Equals(candidate.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                topic = candidate;
                return true;
            }
        }

        return false;
    }
}
