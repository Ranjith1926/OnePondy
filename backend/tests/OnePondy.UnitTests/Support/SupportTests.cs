using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Support;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Support;
using OnePondy.UnitTests.Bookings;
using OnePondy.UnitTests.Food;

namespace OnePondy.UnitTests.Support;

/// <summary>The request itself: the rules that hold wherever it is used.</summary>
public class SupportTicketDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly Guid Agent = Guid.NewGuid();
    private static readonly TimeSpan Window = TimeSpan.FromDays(14);

    private static SupportTicket Ticket() =>
        SupportTicket.Open(Person, null, SupportTopic.Booking, "My booking", "I need to change the date.", null, null, Now);

    [Fact]
    public void A_new_request_is_open_with_the_persons_first_message_and_a_short_reference()
    {
        var ticket = Ticket();

        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Equal("I need to change the date.", Assert.Single(ticket.Messages).Body);
        Assert.Equal(SupportAuthorKind.Requester, ticket.Messages[0].AuthorKind);
        Assert.Equal(8, ticket.Reference.Length);
        Assert.Equal(ticket.Reference.ToUpperInvariant(), ticket.Reference);
        Assert.EndsWith(ticket.Reference, ticket.Id.ToString("N"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_subject_and_a_message_are_needed_and_limited()
    {
        Assert.Throws<ArgumentException>(() => SupportTicket.Open(Person, null, SupportTopic.Other, " ", "x", null, null, Now));
        Assert.Throws<ArgumentException>(() => SupportTicket.Open(Person, null, SupportTopic.Other, "s", " ", null, null, Now));
        Assert.Throws<ArgumentException>(() =>
            SupportTicket.Open(Person, null, SupportTopic.Other, new string('s', SupportTicket.MaxSubjectLength + 1), "x", null, null, Now));
        Assert.Throws<ArgumentException>(() =>
            SupportTicket.Open(Person, null, SupportTopic.Other, "s", new string('x', SupportTicket.MaxBodyLength + 1), null, null, Now));
    }

    [Fact]
    public void A_request_is_about_a_booking_or_an_order_not_both() =>
        Assert.Throws<ArgumentException>(() =>
            SupportTicket.Open(Person, null, SupportTopic.Booking, "s", "x", Guid.NewGuid(), Guid.NewGuid(), Now));

    [Fact]
    public void The_first_answer_waits_for_the_person_stops_the_first_reply_clock_and_takes_the_ticket()
    {
        var ticket = Ticket();

        ticket.AddStaffMessage(Agent, "Of course, which date?", Now.AddHours(2));

        Assert.Equal(SupportTicketStatus.WaitingForRequester, ticket.Status);
        Assert.Equal(Now.AddHours(2), ticket.FirstResponseAt);
        Assert.Equal(Agent, ticket.AssignedToUserId);
        ticket.AddStaffMessage(Guid.NewGuid(), "Any news?", Now.AddHours(30));
        Assert.Equal(Now.AddHours(2), ticket.FirstResponseAt);
        Assert.Equal(Agent, ticket.AssignedToUserId);
    }

    [Fact]
    public void The_person_writing_again_puts_it_back_in_the_queue()
    {
        var ticket = Ticket();
        ticket.AddStaffMessage(Agent, "Which date?", Now.AddHours(1));

        ticket.AddRequesterMessage(Person, "The 12th.", Now.AddHours(2), Window);

        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Equal(Now.AddHours(2), ticket.LastRequesterMessageAt);
        Assert.Equal(3, ticket.Messages.Count);
    }

    [Fact]
    public void A_resolved_request_reopens_within_the_window_and_not_after()
    {
        var ticket = Ticket();
        ticket.Resolve(Agent, Now.AddDays(1));

        ticket.AddRequesterMessage(Person, "Still a problem.", Now.AddDays(10), Window);
        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Null(ticket.ResolvedAt);

        var late = Ticket();
        late.Resolve(Agent, Now.AddDays(1));
        Assert.Throws<InvalidOperationException>(() => late.AddRequesterMessage(Person, "Hello?", Now.AddDays(20), Window));
    }

    [Fact]
    public void Support_cannot_answer_assign_or_escalate_a_resolved_request()
    {
        var ticket = Ticket();
        ticket.Resolve(Agent, Now);

        Assert.Throws<InvalidOperationException>(() => ticket.AddStaffMessage(Agent, "Too late", Now));
        Assert.Throws<InvalidOperationException>(() => ticket.Assign(Agent, Now));
        Assert.Throws<InvalidOperationException>(() => ticket.Escalate("reason", Now));
        Assert.Throws<InvalidOperationException>(() => ticket.Resolve(Agent, Now));
    }

    [Fact]
    public void A_request_is_escalated_once_with_a_reason()
    {
        var ticket = Ticket();
        Assert.Throws<ArgumentException>(() => ticket.Escalate(" ", Now));

        Assert.True(ticket.Escalate("Customer is a partner.", Now));
        Assert.False(ticket.Escalate("Again", Now.AddHours(1)));

        Assert.True(ticket.IsEscalated);
        Assert.Equal("Customer is a partner.", ticket.EscalationReason);
        Assert.Equal(Now, ticket.EscalatedAt);
    }

    [Fact]
    public void Assigning_and_releasing_move_the_ticket_between_agents()
    {
        var ticket = Ticket();
        var other = Guid.NewGuid();

        ticket.Assign(Agent, Now);
        ticket.Assign(other, Now.AddMinutes(1));
        Assert.Equal(other, ticket.AssignedToUserId);

        ticket.Release(Now.AddMinutes(2));
        Assert.Null(ticket.AssignedToUserId);
        Assert.Null(ticket.AssignedAt);
    }
}

/// <summary>Step 10.3: who can ask for help and see what, how agents work the queue, and how nothing waits unseen.</summary>
public class SupportServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly BookingHarness _b = new();
    private readonly SupportOptions _settings = new();
    private readonly SupportService _support;
    private readonly SupportEscalation _escalation;
    private readonly Guid _agent;
    private readonly Guid _otherAgent;
    private int _tickets;

    public SupportServiceTests()
    {
        var audit = new AuditTrail(_b.Db, _b.Time);
        _support = new SupportService(_b.Db, audit, _b.Accounts, Options.Create(_settings), _b.Time);
        _escalation = new SupportEscalation(_b.Db, audit, Options.Create(_settings), _b.Time);
        _b.Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.Support, ModuleStatus.Available));
        _b.Db.SaveChanges();
        _agent = _b.Accounts.FindOrCreateAsync(OtpChannel.Email, "agent@onepondy.test", None).Result.Id;
        _otherAgent = _b.Accounts.FindOrCreateAsync(OtpChannel.Email, "agent2@onepondy.test", None).Result.Id;
        _b.Accounts.SetStaffRole(_agent, RoleNames.SupportAgent);
        _b.Accounts.SetStaffRole(_otherAgent, RoleNames.SupportAgent);
    }

    private Guid Customer => _b.Customer;

    private NewTicketInput Ask(string subject = "My booking", Guid? booking = null, Guid? order = null, Guid? business = null, string topic = "booking") =>
        new(topic, subject, "I need some help, please.", booking, order, business);

    private async Task<TicketDetail> OpenedAsync(Guid? by = null, NewTicketInput? input = null)
    {
        var result = await _support.CreateAsync(by ?? Customer, input ?? Ask($"Request {++_tickets}"), None);
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value!;
    }

    private async Task<uint> VersionAsync(Guid id) => (await _b.Db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == id)).Version;

    private async Task<AgentTicketDetail> AnsweredAsync(Guid id, Guid? agent = null)
    {
        // Support answers some time after the question; messages are ordered by when they were written.
        _b.Time.Advance(TimeSpan.FromMinutes(1));
        var answer = await _support.AgentReplyAsync(id, new AgentMessageInput(await VersionAsync(id), "Happy to help."), agent ?? _agent, None);
        Assert.True(answer.IsSuccess, answer.Error?.Detail);
        return answer.Value!;
    }

    private async Task<int> AuditCountAsync(string action) =>
        await _b.Db.AuditEntries.AsNoTracking().CountAsync(a => a.Action == action);

    // --- Asking for help ---

    [Fact]
    public async Task A_customer_can_ask_for_help_and_sees_their_own_request()
    {
        var created = await _support.CreateAsync(Customer, Ask(), None);

        Assert.True(created.IsSuccess, created.Error?.Detail);
        var ticket = created.Value!;
        Assert.Equal(SupportTicketStatus.Open, ticket.Status);
        Assert.Equal(SupportTopic.Booking, ticket.Topic);
        Assert.Single(ticket.Messages);
        Assert.False(ticket.Messages[0].FromSupport);
        Assert.True(ticket.CanReply);
        Assert.Equal(ticket.Id, Assert.Single(await _support.ListMineAsync(Customer, None)).Id);
    }

    [Fact]
    public async Task Requests_are_not_opened_until_the_support_module_is_switched_on()
    {
        var flag = await _b.Db.ModuleFlags.SingleAsync(f => f.Key == ModuleKeys.Support);
        _b.Db.ModuleFlags.Remove(flag);
        await _b.Db.SaveChangesAsync();

        var result = await _support.CreateAsync(Customer, Ask(), None);

        Assert.Equal("support_unavailable", result.Error!.Code);
        Assert.Empty(await _b.Db.SupportTickets.ToListAsync());
    }

    [Fact]
    public async Task A_subject_a_message_and_a_topic_are_needed()
    {
        var result = await _support.CreateAsync(Customer, new NewTicketInput("nonsense", " ", null, null, null, null), None);

        Assert.Equal("validation_failed", result.Error!.Code);
        Assert.Contains("subject", result.Error.FieldErrors!.Keys);
        Assert.Contains("message", result.Error.FieldErrors.Keys);
        Assert.Contains("topic", result.Error.FieldErrors.Keys);
    }

    [Theory]
    [InlineData("foodOrder")]
    [InlineData("food_order")]
    [InlineData("FoodOrder")]
    public async Task A_topic_is_read_however_it_is_written(string topic)
    {
        var ticket = await OpenedAsync(input: Ask(topic: topic));

        Assert.Equal(SupportTopic.FoodOrder, ticket.Topic);
    }

    [Fact]
    public async Task One_person_cannot_have_more_than_five_open_requests_and_a_resolved_one_does_not_count()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < _settings.MaxOpenTicketsPerUser; i++)
        {
            ids.Add((await OpenedAsync()).Id);
        }

        var refused = await _support.CreateAsync(Customer, Ask(), None);
        Assert.Equal("support_too_many_open", refused.Error!.Code);

        Assert.True((await _support.ResolveAsync(ids[0], await VersionAsync(ids[0]), _agent, None)).IsSuccess);
        Assert.True((await _support.CreateAsync(Customer, Ask(), None)).IsSuccess);
    }

    [Fact]
    public async Task A_request_can_be_about_the_customers_own_booking_and_support_sees_it_in_a_few_lines()
    {
        var booking = (await _b.Bookings.CreateAsync(Customer, _b.Input(), None)).Value!;

        var ticket = await OpenedAsync(input: Ask(booking: booking.Id));

        Assert.Equal("booking", ticket.About!.Kind);
        Assert.Equal("Backwater Kayaking", ticket.About.Title);
        Assert.Equal(booking.Id, ticket.About.Id);
        Assert.Equal("pending", ticket.About.Status);
    }

    [Fact]
    public async Task Someone_elses_booking_cannot_be_linked_and_is_not_revealed()
    {
        var booking = (await _b.Bookings.CreateAsync(Customer, _b.Input(), None)).Value!;
        var stranger = (await _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000011111", None)).Id;

        var result = await _support.CreateAsync(stranger, Ask(booking: booking.Id), None);

        Assert.Equal("support_linked_not_found", result.Error!.Code);
        Assert.Empty(await _b.Db.SupportTickets.ToListAsync());
    }

    [Fact]
    public async Task A_request_cannot_be_about_a_booking_and_an_order_at_once()
    {
        var result = await _support.CreateAsync(Customer, Ask(booking: Guid.NewGuid(), order: Guid.NewGuid()), None);

        Assert.Equal("validation_failed", result.Error!.Code);
        Assert.Contains("bookingId", result.Error.FieldErrors!.Keys);
    }

    [Fact]
    public async Task A_business_owner_can_write_about_their_business_and_its_bookings_but_no_one_elses()
    {
        var booking = (await _b.Bookings.CreateAsync(Customer, _b.Input(), None)).Value!;

        var ticket = await OpenedAsync(_b.Owner, Ask(business: _b.BusinessId, booking: booking.Id, topic: "business"));
        var agentView = (await _support.AgentGetAsync(ticket.Id, _agent, None)).Value!.Ticket;
        Assert.Equal("partner", agentView.RequesterKind);
        Assert.NotNull(agentView.BusinessName);

        var stranger = (await _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000022222", None)).Id;
        var notTheirs = await _support.CreateAsync(stranger, Ask(business: _b.BusinessId, topic: "business"), None);
        Assert.Equal("ticket_not_found", notTheirs.Error!.Code);

        // The customer is not the business: the booking is theirs, but it is not their business's request to make.
        var asCustomerOfOthers = await _support.CreateAsync(_b.Owner, Ask(booking: booking.Id), None);
        Assert.Equal("support_linked_not_found", asCustomerOfOthers.Error!.Code);
    }

    [Fact]
    public async Task A_delivered_order_can_be_linked_and_summarised()
    {
        var food = new FoodOrderHarness();
        food.Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.Support, ModuleStatus.Available));
        await food.OpenAsync(paymentsOn: false);
        var service = new SupportService(food.Db, food.B.Audit, food.B.Accounts, Options.Create(_settings), food.B.Time);
        var order = await food.PlacedAsync("support-order");

        var created = await service.CreateAsync(food.Buyer, Ask(order: order.Id, topic: "foodOrder"), None);
        var stranger = await service.CreateAsync(food.Stranger, Ask(order: order.Id, topic: "foodOrder"), None);

        Assert.Equal("food_order", created.Value!.About!.Kind);
        Assert.Equal("Spice Route", created.Value.About.Title);
        Assert.Equal(339.25m, created.Value.About.TotalAmount);
        Assert.Equal("support_linked_not_found", stranger.Error!.Code);
    }

    // --- Seeing only your own ---

    [Fact]
    public async Task A_person_sees_only_their_own_requests()
    {
        var mine = await OpenedAsync();
        var stranger = (await _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000033333", None)).Id;
        await OpenedAsync(stranger);

        Assert.Equal(mine.Id, Assert.Single(await _support.ListMineAsync(Customer, None)).Id);
        Assert.Equal("ticket_not_found", (await _support.GetMineAsync(mine.Id, stranger, None)).Error!.Code);
        Assert.Equal(
            "ticket_not_found",
            (await _support.ReplyAsync(mine.Id, stranger, new MessageInput("Hello"), None)).Error!.Code);
    }

    [Fact]
    public async Task The_person_never_sees_which_staff_member_answered()
    {
        var ticket = await OpenedAsync();
        await AnsweredAsync(ticket.Id);

        var seen = (await _support.GetMineAsync(ticket.Id, Customer, None)).Value!;

        Assert.Equal(2, seen.Messages.Count);
        Assert.True(seen.Messages[1].FromSupport);
        Assert.DoesNotContain(typeof(TicketMessageView).GetProperties(), p => p.Name.Contains("Author", StringComparison.Ordinal));
        Assert.Equal(SupportTicketStatus.WaitingForRequester, seen.Status);
    }

    // --- Replying ---

    [Fact]
    public async Task The_person_replying_puts_it_back_in_the_queue()
    {
        var ticket = await OpenedAsync();
        await AnsweredAsync(ticket.Id);

        var after = (await _support.ReplyAsync(ticket.Id, Customer, new MessageInput("Thanks, the 12th."), None)).Value!;

        Assert.Equal(SupportTicketStatus.Open, after.Status);
        Assert.Equal(3, after.Messages.Count);
        Assert.Equal("validation_failed", (await _support.ReplyAsync(ticket.Id, Customer, new MessageInput(" "), None)).Error!.Code);
    }

    [Fact]
    public async Task A_thread_cannot_grow_without_end()
    {
        _settings.MaxMessagesPerTicket = 3;
        var ticket = await OpenedAsync();
        Assert.True((await _support.ReplyAsync(ticket.Id, Customer, new MessageInput("two"), None)).IsSuccess);
        Assert.True((await _support.ReplyAsync(ticket.Id, Customer, new MessageInput("three"), None)).IsSuccess);

        var refused = await _support.ReplyAsync(ticket.Id, Customer, new MessageInput("four"), None);

        Assert.Equal("support_too_many_messages", refused.Error!.Code);
    }

    [Fact]
    public async Task A_resolved_request_can_be_reopened_for_two_weeks_and_then_a_new_one_is_needed()
    {
        var ticket = await OpenedAsync();
        Assert.True((await _support.ResolveAsync(ticket.Id, await VersionAsync(ticket.Id), _agent, None)).IsSuccess);

        _b.Time.Advance(TimeSpan.FromDays(10));
        var reopened = await _support.ReplyAsync(ticket.Id, Customer, new MessageInput("It happened again."), None);
        Assert.Equal(SupportTicketStatus.Open, reopened.Value!.Status);

        Assert.True((await _support.ResolveAsync(ticket.Id, await VersionAsync(ticket.Id), _agent, None)).IsSuccess);
        _b.Time.Advance(TimeSpan.FromDays(15));
        var late = await _support.ReplyAsync(ticket.Id, Customer, new MessageInput("Hello again?"), None);
        Assert.Equal("support_cannot_reopen", late.Error!.Code);
        Assert.False((await _support.GetMineAsync(ticket.Id, Customer, None)).Value!.CanReply);
    }

    // --- Agents ---

    [Fact]
    public async Task An_agent_answering_takes_the_request_and_it_waits_for_the_person()
    {
        var ticket = await OpenedAsync();

        var answered = await AnsweredAsync(ticket.Id);

        Assert.Equal(SupportTicketStatus.WaitingForRequester, answered.Ticket.Status);
        Assert.Equal(_agent, answered.Ticket.AssignedToUserId);
        Assert.Equal(["requester", "staff"], answered.Messages.Select(m => m.Kind));
    }

    [Fact]
    public async Task Support_cannot_answer_a_resolved_request()
    {
        var ticket = await OpenedAsync();
        await _support.ResolveAsync(ticket.Id, await VersionAsync(ticket.Id), _agent, None);

        var late = await _support.AgentReplyAsync(ticket.Id, new AgentMessageInput(await VersionAsync(ticket.Id), "Hello"), _agent, None);

        Assert.Equal("support_resolved", late.Error!.Code);
    }

    [Fact]
    public async Task The_agents_lists_show_the_right_requests()
    {
        var needs = await OpenedAsync();
        var waiting = await OpenedAsync();
        await AnsweredAsync(waiting.Id, _agent);
        var resolved = await OpenedAsync();
        await _support.ResolveAsync(resolved.Id, await VersionAsync(resolved.Id), _agent, None);
        var unassigned = needs.Id;
        var escalated = await OpenedAsync();
        await _support.EscalateAsync(escalated.Id, new EscalateInput(await VersionAsync(escalated.Id), "Angry customer."), _otherAgent, None);

        async Task<List<Guid>> ListAsync(string? view, Guid? as_ = null) =>
            [.. (await _support.AgentListAsync(new AgentTicketQuery(view, null, null), as_ ?? _agent, None)).Value!.Items.Select(t => t.Id)];

        var queue = await ListAsync(null);
        Assert.Contains(needs.Id, queue);
        Assert.Contains(escalated.Id, queue);
        Assert.DoesNotContain(waiting.Id, queue);
        Assert.DoesNotContain(resolved.Id, queue);
        Assert.Equal([waiting.Id], await ListAsync("waiting"));
        Assert.Equal([resolved.Id], await ListAsync("resolved"));
        Assert.Equal([escalated.Id], await ListAsync("escalated"));
        Assert.Equal([waiting.Id], await ListAsync("mine"));
        Assert.Empty(await ListAsync("mine", _otherAgent));
        Assert.Contains(unassigned, await ListAsync("unassigned"));
        Assert.DoesNotContain(waiting.Id, await ListAsync("unassigned"));
        Assert.Equal(4, (await ListAsync("all")).Count);
        Assert.Equal("validation_failed", (await _support.AgentListAsync(new AgentTicketQuery("everything", null, null), _agent, None)).Error!.Code);
    }

    [Fact]
    public async Task The_queue_puts_escalated_requests_first_and_then_the_one_waiting_longest()
    {
        var first = await OpenedAsync();
        _b.Time.Advance(TimeSpan.FromMinutes(5));
        var second = await OpenedAsync();
        _b.Time.Advance(TimeSpan.FromMinutes(5));
        var third = await OpenedAsync();
        await _support.EscalateAsync(third.Id, new EscalateInput(await VersionAsync(third.Id), "VIP."), _agent, None);

        var queue = (await _support.AgentListAsync(new AgentTicketQuery(null, null, null), _agent, None)).Value!.Items;

        Assert.Equal([third.Id, first.Id, second.Id], queue.Select(t => t.Id));
    }

    [Fact]
    public async Task An_agent_sees_the_first_name_and_what_it_is_about_and_not_the_persons_phone_number()
    {
        await _b.Accounts.UpdateProfileAsync(Customer, "Anita Rao", null, None);
        var booking = (await _b.Bookings.CreateAsync(Customer, _b.Input(), None)).Value!;
        var ticket = await OpenedAsync(input: Ask(booking: booking.Id));

        var seen = (await _support.AgentGetAsync(ticket.Id, _agent, None)).Value!;

        Assert.Equal("Anita", seen.Ticket.RequesterName);
        Assert.Equal("Anita", seen.Messages[0].AuthorName);
        Assert.Equal("Backwater Kayaking", seen.About!.Title);
        Assert.DoesNotContain(
            typeof(AgentTicketDetail).GetProperties().Concat(typeof(AgentTicketSummary).GetProperties()),
            p => p.Name.Contains("Phone", StringComparison.Ordinal) || p.Name.Contains("Email", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_name_that_is_really_a_number_is_not_shown_to_the_agent()
    {
        await _b.Accounts.UpdateProfileAsync(Customer, "9988776655", null, None);
        var ticket = await OpenedAsync();

        var seen = (await _support.AgentGetAsync(ticket.Id, _agent, None)).Value!;

        Assert.Equal("A customer", seen.Ticket.RequesterName);
    }

    [Fact]
    public async Task Every_time_an_agent_opens_a_request_it_is_audited()
    {
        var ticket = await OpenedAsync();

        await _support.AgentGetAsync(ticket.Id, _agent, None);
        await _support.AgentGetAsync(ticket.Id, _otherAgent, None);

        var entries = await _b.Db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == AuditActions.SupportTicketViewed).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { _agent, _otherAgent }.Order(), entries.Select(e => e.ActorUserId!.Value).Order());
        Assert.All(entries, e => Assert.Equal(ticket.Id.ToString(), e.EntityId));
        Assert.Equal("ticket_not_found", (await _support.AgentGetAsync(Guid.NewGuid(), _agent, None)).Error!.Code);
    }

    [Fact]
    public async Task The_persons_contact_details_are_a_separate_audited_step()
    {
        var ticket = await OpenedAsync();
        await _support.AgentGetAsync(ticket.Id, _agent, None);
        Assert.Equal(0, await AuditCountAsync(AuditActions.SupportContactViewed));

        var contact = (await _support.ContactAsync(ticket.Id, _agent, None)).Value!;

        Assert.Equal("+919988776655", contact.PhoneNumber);
        Assert.Equal(1, await AuditCountAsync(AuditActions.SupportContactViewed));
        Assert.Equal("ticket_not_found", (await _support.ContactAsync(Guid.NewGuid(), _agent, None)).Error!.Code);
    }

    [Fact]
    public async Task Taking_releasing_and_resolving_are_audited_and_resolving_closes_the_request()
    {
        var ticket = await OpenedAsync();

        var taken = (await _support.AssignAsync(ticket.Id, await VersionAsync(ticket.Id), _agent, None)).Value!;
        Assert.Equal(_agent, taken.Ticket.AssignedToUserId);
        var released = (await _support.ReleaseAsync(ticket.Id, await VersionAsync(ticket.Id), _agent, None)).Value!;
        Assert.Null(released.Ticket.AssignedToUserId);
        var resolved = (await _support.ResolveAsync(ticket.Id, await VersionAsync(ticket.Id), _agent, None)).Value!;

        Assert.Equal(SupportTicketStatus.Resolved, resolved.Ticket.Status);
        Assert.Equal(_agent, resolved.ResolvedByUserId);
        Assert.Equal(1, await AuditCountAsync(AuditActions.SupportTicketAssigned));
        Assert.Equal(1, await AuditCountAsync(AuditActions.SupportTicketReleased));
        Assert.Equal(1, await AuditCountAsync(AuditActions.SupportTicketResolved));
        Assert.Equal("support_resolved", (await _support.ResolveAsync(ticket.Id, await VersionAsync(ticket.Id), _agent, None)).Error!.Code);
    }

    [Fact]
    public async Task An_agent_can_escalate_once_with_a_reason()
    {
        var ticket = await OpenedAsync();
        var noReason = await _support.EscalateAsync(ticket.Id, new EscalateInput(0, "  "), _agent, None);
        Assert.Contains("reason", noReason.Error!.FieldErrors!.Keys);

        var escalated = (await _support.EscalateAsync(ticket.Id, new EscalateInput(await VersionAsync(ticket.Id), "Refund dispute."), _agent, None)).Value!;
        var again = await _support.EscalateAsync(ticket.Id, new EscalateInput(await VersionAsync(ticket.Id), "Again"), _agent, None);

        Assert.True(escalated.Ticket.Escalated);
        Assert.Equal("Refund dispute.", escalated.Ticket.EscalationReason);
        Assert.Equal("support_already_escalated", again.Error!.Code);
        Assert.Equal(1, await AuditCountAsync(AuditActions.SupportTicketEscalated));
    }

    // --- Time targets ---

    [Fact]
    public async Task A_request_is_flagged_when_it_misses_the_first_reply_target_until_it_is_answered()
    {
        var ticket = await OpenedAsync();
        _b.Time.Advance(TimeSpan.FromHours(23));
        Assert.False((await _support.AgentGetAsync(ticket.Id, _agent, None)).Value!.Ticket.FirstReplyOverdue);

        _b.Time.Advance(TimeSpan.FromHours(2));
        Assert.True((await _support.AgentGetAsync(ticket.Id, _agent, None)).Value!.Ticket.FirstReplyOverdue);
        Assert.Contains(ticket.Id, (await _support.AgentListAsync(new AgentTicketQuery("overdue", null, null), _agent, None)).Value!.Items.Select(t => t.Id));

        await AnsweredAsync(ticket.Id);
        Assert.False((await _support.AgentGetAsync(ticket.Id, _agent, None)).Value!.Ticket.FirstReplyOverdue);
    }

    [Fact]
    public async Task A_request_is_flagged_when_it_is_still_unresolved_after_five_days()
    {
        var ticket = await OpenedAsync();
        await AnsweredAsync(ticket.Id);
        _b.Time.Advance(TimeSpan.FromDays(6));

        var seen = (await _support.AgentGetAsync(ticket.Id, _agent, None)).Value!.Ticket;

        Assert.False(seen.FirstReplyOverdue);
        Assert.True(seen.ResolutionOverdue);
        Assert.Equal(1, (await _support.CountsAsync(None)).Overdue);
    }

    [Fact]
    public async Task The_Worker_escalates_a_request_that_missed_a_target_once_and_says_why()
    {
        var unanswered = await OpenedAsync();
        var answered = await OpenedAsync();
        await AnsweredAsync(answered.Id);
        var resolved = await OpenedAsync();
        await _support.ResolveAsync(resolved.Id, await VersionAsync(resolved.Id), _agent, None);
        _b.Time.Advance(TimeSpan.FromHours(25));

        Assert.Equal(1, await _escalation.EscalateOverdueAsync(None));
        Assert.Equal(0, await _escalation.EscalateOverdueAsync(None));

        var tickets = await _b.Db.SupportTickets.AsNoTracking().ToListAsync();
        var one = tickets.Single(t => t.Id == unanswered.Id);
        Assert.Equal(SupportTicket.Escalations.FirstReplyOverdue, one.EscalationReason);
        Assert.False(tickets.Single(t => t.Id == answered.Id).IsEscalated);
        Assert.False(tickets.Single(t => t.Id == resolved.Id).IsEscalated);
        var entry = await _b.Db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == AuditActions.SupportTicketEscalated);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(unanswered.Id.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task The_Worker_escalates_an_answered_request_still_open_after_the_resolution_target()
    {
        var ticket = await OpenedAsync();
        await AnsweredAsync(ticket.Id);
        _b.Time.Advance(TimeSpan.FromDays(6));

        Assert.Equal(1, await _escalation.EscalateOverdueAsync(None));

        var stored = await _b.Db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(SupportTicket.Escalations.ResolutionOverdue, stored.EscalationReason);
    }

    [Fact]
    public async Task The_counts_say_what_needs_attention()
    {
        var a = await OpenedAsync();
        var b = await OpenedAsync();
        await AnsweredAsync(b.Id);
        await _support.EscalateAsync(a.Id, new EscalateInput(await VersionAsync(a.Id), "VIP"), _agent, None);

        var counts = await _support.CountsAsync(None);

        Assert.Equal(new SupportCounts(NeedsReply: 1, Waiting: 1, Unassigned: 1, Overdue: 0, Escalated: 1), counts);
    }

    // --- The grievance officer ---

    [Fact]
    public void The_grievance_officer_is_not_shown_until_one_is_appointed()
    {
        Assert.False(_support.Grievance().Configured);
        Assert.Null(_support.Grievance().Name);

        _settings.GrievanceOfficer.Name = "R. Kumar";
        Assert.False(_support.Grievance().Configured);

        _settings.GrievanceOfficer.Email = "grievance@onepondy.example";
        _settings.GrievanceOfficer.Address = "Puducherry";
        var view = _support.Grievance();

        Assert.True(view.Configured);
        Assert.Equal("R. Kumar", view.Name);
        Assert.Equal(48, view.AcknowledgeWithinHours);
        Assert.Equal(30, view.ResolveWithinDays);
    }
}
