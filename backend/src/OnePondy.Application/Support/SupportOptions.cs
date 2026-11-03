namespace OnePondy.Application.Support;

/// <summary>Settings for support (spec Phase 10, decisions R9 to R11 in docs/phase-10-plan.md).</summary>
public sealed class SupportOptions
{
    public const string SectionName = "Support";

    /// <summary>How soon support should first answer a request. A request still unanswered after this is flagged and escalated.</summary>
    public TimeSpan FirstReplyTarget { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How soon a request should be resolved. One still unresolved after this is flagged and escalated.</summary>
    public TimeSpan ResolutionTarget { get; set; } = TimeSpan.FromDays(5);

    /// <summary>How long after a request is resolved the person can reopen it by replying. After that they start a new one.</summary>
    public TimeSpan ReopenWindow { get; set; } = TimeSpan.FromDays(14);

    /// <summary>The most unresolved requests one person can have, so one person cannot bury the queue.</summary>
    public int MaxOpenTicketsPerUser { get; set; } = 5;

    /// <summary>The most messages in one request, so a thread cannot grow without end.</summary>
    public int MaxMessagesPerTicket { get; set; } = 100;

    /// <summary>
    /// The grievance officer the Consumer Protection (E-Commerce) Rules require, shown publicly. Empty until the owner appoints someone; the
    /// app and the public page then say it is not set up rather than showing a blank.
    /// </summary>
    public GrievanceOfficerOptions GrievanceOfficer { get; set; } = new();
}

public sealed class GrievanceOfficerOptions
{
    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    /// <summary>What the Rules ask for is to acknowledge a complaint within 48 hours and resolve it within a month. Confirm with a lawyer before launch.</summary>
    public int AcknowledgeWithinHours { get; set; } = 48;

    public int ResolveWithinDays { get; set; } = 30;
}
