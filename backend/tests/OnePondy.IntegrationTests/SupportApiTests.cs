using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OnePondy.Application.Support;

namespace OnePondy.IntegrationTests;

// Support requests (spec Phase 10, step 10.3), against a real PostgreSQL database: the HTTP flow from a customer's request to an agent's answer,
// who may do what, the agents' queues (whose queries only PostgreSQL can really check), the Worker's escalation of a request that waited too long,
// and the rules only the database can enforce. Part of FoodOrderApiTests so these tests share its sign-in and restaurant helpers.
public sealed partial class FoodOrderApiTests
{
    private const string Support = "/api/v1/support";

    private const string AdminSupport = "/api/v1/admin/support";

    private async Task SetSupportModuleAsync(HttpClient client, string admin, string status)
    {
        var response = await SendAsync(client, HttpMethod.Put, "/api/v1/admin/config/modules/support", admin, new { status });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> AskAsync(HttpClient client, string token, object body)
    {
        var response = await SendAsync(client, HttpMethod.Post, $"{Support}/tickets", token, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Question(string subject = "Where is my order?", string topic = "foodOrder") =>
        new { topic, subject, message = "I ordered a while ago and nothing has arrived." };

    /// <summary>Opens a request as a new person, and returns its id and their token.</summary>
    private async Task<(string Id, string Customer)> OpenRequestAsync(HttpClient client)
    {
        var customer = await SignInAsync(client);
        var ticket = await AskAsync(client, customer, Question());
        return (Id(ticket), customer);
    }

    private static async Task<JsonElement> AgentTicketAsync(HttpClient client, string agent, string id) =>
        await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/tickets/{id}", agent));

    [DatabaseFact]
    public async Task A_customer_asks_for_help_and_an_agent_answers_in_the_console()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var agent = await StaffTokenAsync("support_agent");
        var editor = await StaffTokenAsync("content_editor");
        var customer = await SignInAsync(client);
        var stranger = await SignInAsync(client);

        // Nothing opens until the support module is switched on.
        await SetSupportModuleAsync(client, admin, "comingSoon");
        var closed = await SendAsync(client, HttpMethod.Post, $"{Support}/tickets", customer, Question());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
        Assert.Equal("support_unavailable", await ErrorCodeAsync(closed));
        await SetSupportModuleAsync(client, admin, "available");
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"{Support}/tickets", Question())).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, $"{Support}/tickets", customer, new { topic = "other", subject = " ", message = "" })).StatusCode);

            var ticket = await AskAsync(client, customer, Question());
            var id = Id(ticket);
            Assert.Equal("open", ticket.GetProperty("status").GetString());
            Assert.Equal(8, ticket.GetProperty("reference").GetString()!.Length);

            // Only the author sees it.
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"{Support}/tickets/{id}", stranger)).StatusCode);
            Assert.DoesNotContain(
                (await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{Support}/tickets", stranger))).EnumerateArray(),
                t => Id(t) == id);
            Assert.Single((await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{Support}/tickets", customer))).EnumerateArray(), t => Id(t) == id);

            // Only support agents and administrators work the queue: customers and content editors are refused.
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/tickets", customer)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/tickets", editor)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{AdminSupport}/tickets")).StatusCode);

            // The agent finds it in the queue, opens it (audited), and sees the first name and no phone number.
            var queue = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/tickets?pageSize=100", agent));
            Assert.Contains(queue.GetProperty("items").EnumerateArray(), t => Id(t) == id);
            var detail = await AgentTicketAsync(client, agent, id);
            Assert.Equal(1, await AuditCountAsync("support_ticket.viewed", id));
            Assert.DoesNotContain("+91", detail.GetRawText());
            var contact = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/tickets/{id}/contact", agent));
            Assert.StartsWith("+91", contact.GetProperty("phoneNumber").GetString());
            Assert.Equal(1, await AuditCountAsync("support_ticket.contact_viewed", id));

            // The agent answers: it waits for the customer, who sees the answer without knowing who wrote it.
            var answered = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{AdminSupport}/tickets/{id}/messages", agent, new { version = VersionOf(detail), body = "Looking into it now." }));
            Assert.Equal("waitingForRequester", answered.GetProperty("ticket").GetProperty("status").GetString());
            var mine = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{Support}/tickets/{id}", customer));
            Assert.Equal(2, mine.GetProperty("messages").GetArrayLength());
            Assert.True(mine.GetProperty("messages")[1].GetProperty("fromSupport").GetBoolean());
            Assert.DoesNotContain("authorUserId", mine.GetRawText());

            // The customer replies: back in the queue. The agent resolves it; the customer can still reopen it.
            var replied = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{Support}/tickets/{id}/messages", customer, new { body = "Thank you, any news?" }));
            Assert.Equal("open", replied.GetProperty("status").GetString());
            var resolved = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{AdminSupport}/tickets/{id}/resolve", agent, new { version = VersionOf(await AgentTicketAsync(client, agent, id)) }));
            Assert.Equal("resolved", resolved.GetProperty("ticket").GetProperty("status").GetString());
            Assert.Equal(1, await AuditCountAsync("support_ticket.resolved", id));
            var late = await SendAsync(client, HttpMethod.Post, $"{AdminSupport}/tickets/{id}/messages", agent, new { version = VersionOf(await AgentTicketAsync(client, agent, id)), body = "Hello?" });
            Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
            Assert.Equal("support_resolved", await ErrorCodeAsync(late));
            var reopened = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{Support}/tickets/{id}/messages", customer, new { body = "It is still not here." }));
            Assert.Equal("open", reopened.GetProperty("status").GetString());

            // The agent escalates it, and the counts show it.
            var escalated = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{AdminSupport}/tickets/{id}/escalate", agent, new { version = VersionOf(await AgentTicketAsync(client, agent, id)), reason = "Missing order, third message." }));
            Assert.True(escalated.GetProperty("ticket").GetProperty("escalated").GetBoolean());
            var counts = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/counts", admin));
            Assert.True(counts.GetProperty("escalated").GetInt32() >= 1);

            // Taking and releasing it are audited too.
            var taken = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{AdminSupport}/tickets/{id}/assign", agent, new { version = VersionOf(await AgentTicketAsync(client, agent, id)) }));
            Assert.NotEqual(JsonValueKind.Null, taken.GetProperty("ticket").GetProperty("assignedToUserId").ValueKind);
            Assert.Equal(1, await AuditCountAsync("support_ticket.assigned", id));
        }
        finally
        {
            await SetSupportModuleAsync(client, admin, "comingSoon");
        }
    }

    [DatabaseFact]
    public async Task A_business_owner_writes_about_their_business_and_no_one_elses()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        await SetSupportModuleAsync(client, admin, "available");
        try
        {
            var shop = await OpenShopAsync(client, admin, UniqueName("Support Samosas"));
            var stranger = await SignInAsync(client);

            var ticket = await AskAsync(client, shop.Owner, new { topic = "business", subject = "Menu question", message = "How do I hide a dish?", businessId = shop.BusinessId });
            var agent = await StaffTokenAsync("support_agent");
            var seen = await AgentTicketAsync(client, agent, Id(ticket));

            Assert.Equal("partner", seen.GetProperty("ticket").GetProperty("requesterKind").GetString());
            Assert.StartsWith("Support Samosas", seen.GetProperty("ticket").GetProperty("businessName").GetString());
            var refused = await SendAsync(client, HttpMethod.Post, $"{Support}/tickets", stranger, new { topic = "business", subject = "Not mine", message = "Hi", businessId = shop.BusinessId });
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }
        finally
        {
            await SetSupportModuleAsync(client, admin, "comingSoon");
        }
    }

    [DatabaseFact]
    public async Task Every_list_the_agents_can_ask_for_is_answered_by_PostgreSQL()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var agent = await StaffTokenAsync("support_agent");
        await SetSupportModuleAsync(client, admin, "available");
        try
        {
            var (id, _) = await OpenRequestAsync(client);

            foreach (var view in new[] { "needs_reply", "waiting", "mine", "unassigned", "escalated", "overdue", "resolved", "all" })
            {
                var response = await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/tickets?view={view}", agent);
                Assert.True(response.IsSuccessStatusCode, $"{view}: {await response.Content.ReadAsStringAsync()}");
            }

            var unassigned = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/tickets?view=unassigned&pageSize=100", agent));
            Assert.Contains(unassigned.GetProperty("items").EnumerateArray(), t => Id(t) == id);
            var bad = await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/tickets?view=everything", agent);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        finally
        {
            await SetSupportModuleAsync(client, admin, "comingSoon");
        }
    }

    [DatabaseFact]
    public async Task A_request_that_waited_too_long_is_flagged_and_escalated_by_the_Worker()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var agent = await StaffTokenAsync("support_agent");
        await SetSupportModuleAsync(client, admin, "available");
        try
        {
            var (id, _) = await OpenRequestAsync(client);
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                await DbOf(scope).Database.ExecuteSqlRawAsync(
                    "UPDATE support_tickets SET created_at = now() - interval '2 days', last_requester_message_at = now() - interval '2 days' WHERE id = {0}",
                    Guid.Parse(id));
            }

            var flagged = (await AgentTicketAsync(client, agent, id)).GetProperty("ticket");
            Assert.True(flagged.GetProperty("firstReplyOverdue").GetBoolean());
            Assert.False(flagged.GetProperty("resolutionOverdue").GetBoolean());
            var overdue = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{AdminSupport}/tickets?view=overdue&pageSize=100", agent));
            Assert.Contains(overdue.GetProperty("items").EnumerateArray(), t => Id(t) == id);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var escalated = await scope.ServiceProvider.GetRequiredService<SupportEscalation>().EscalateOverdueAsync(default);
                Assert.True(escalated >= 1);
            }

            var after = (await AgentTicketAsync(client, agent, id)).GetProperty("ticket");
            Assert.True(after.GetProperty("escalated").GetBoolean());
            Assert.Equal("first_reply_overdue", after.GetProperty("escalationReason").GetString());
            Assert.Equal(1, await AuditCountAsync("support_ticket.escalated", id));
            await using var again = Factory.Services.CreateAsyncScope();
            await again.ServiceProvider.GetRequiredService<SupportEscalation>().EscalateOverdueAsync(default);
            Assert.Equal(1, await AuditCountAsync("support_ticket.escalated", id));
        }
        finally
        {
            await SetSupportModuleAsync(client, admin, "comingSoon");
        }
    }

    [DatabaseFact]
    public async Task Anyone_can_read_who_the_grievance_officer_is_and_it_says_when_none_is_appointed()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync($"{Support}/grievance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(view.GetProperty("configured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("name").ValueKind);
        Assert.Equal(48, view.GetProperty("acknowledgeWithinHours").GetInt32());
    }

    [DatabaseFact]
    public async Task The_database_refuses_a_request_the_service_would_never_write()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await SetSupportModuleAsync(client, admin, "available");
        try
        {
            var (id, _) = await OpenRequestAsync(client);

            await using var scope = Factory.Services.CreateAsyncScope();
            await using var sql = new NpgsqlConnection(DbOf(scope).Database.GetConnectionString());
            await sql.OpenAsync();

            async Task<string?> RefusedAsync(string text)
            {
                await using var command = new NpgsqlCommand(text, sql);
                command.Parameters.AddWithValue("id", Guid.Parse(id));
                var refused = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
                return refused.ConstraintName;
            }

            Assert.Equal("ck_support_ticket_status", await RefusedAsync("UPDATE support_tickets SET status = 'closed' WHERE id = @id"));
            Assert.Equal("ck_support_ticket_topic", await RefusedAsync("UPDATE support_tickets SET topic = 'gossip' WHERE id = @id"));
            Assert.Equal("ck_support_ticket_subject", await RefusedAsync("UPDATE support_tickets SET subject = '' WHERE id = @id"));

            // Resolved means resolved at a time, by someone.
            Assert.Equal("ck_support_ticket_resolved", await RefusedAsync("UPDATE support_tickets SET status = 'resolved' WHERE id = @id"));
            Assert.Equal("ck_support_ticket_resolved", await RefusedAsync("UPDATE support_tickets SET resolved_at = now() WHERE id = @id"));

            // Assigned is a person and a time; escalated is a time and a reason.
            Assert.Equal("ck_support_ticket_assigned", await RefusedAsync("UPDATE support_tickets SET assigned_at = now() WHERE id = @id"));
            Assert.Equal("ck_support_ticket_escalated", await RefusedAsync("UPDATE support_tickets SET escalated_at = now() WHERE id = @id"));

            // A message needs words, and a known author kind.
            Assert.Equal(
                "ck_support_message_body",
                await RefusedAsync("INSERT INTO support_messages (id, ticket_id, author_user_id, author_kind, body, created_at) SELECT gen_random_uuid(), id, requester_user_id, 'requester', '', now() FROM support_tickets WHERE id = @id"));
            Assert.Equal(
                "ck_support_message_author_kind",
                await RefusedAsync("INSERT INTO support_messages (id, ticket_id, author_user_id, author_kind, body, created_at) SELECT gen_random_uuid(), id, requester_user_id, 'robot', 'hi', now() FROM support_tickets WHERE id = @id"));

            // Deleting a request takes its messages with it.
            await using (var count = new NpgsqlCommand("SELECT count(*) FROM support_messages WHERE ticket_id = @id", sql))
            {
                count.Parameters.AddWithValue("id", Guid.Parse(id));
                Assert.Equal(1L, await count.ExecuteScalarAsync());
            }

            await using (var delete = new NpgsqlCommand("DELETE FROM support_tickets WHERE id = @id", sql))
            {
                delete.Parameters.AddWithValue("id", Guid.Parse(id));
                Assert.Equal(1, await delete.ExecuteNonQueryAsync());
            }

            await using var left = new NpgsqlCommand("SELECT count(*) FROM support_messages WHERE ticket_id = @id", sql);
            left.Parameters.AddWithValue("id", Guid.Parse(id));
            Assert.Equal(0L, await left.ExecuteScalarAsync());
        }
        finally
        {
            await SetSupportModuleAsync(client, admin, "comingSoon");
        }
    }
}
