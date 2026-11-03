using System.Net;
using System.Text.Json;

namespace OnePondy.IntegrationTests;

// The operations page's data (spec Phase 10, step 10.5), against a real PostgreSQL database: who may read it, and that every part of it is
// answered by PostgreSQL. Part of FoodOrderApiTests so these tests share its sign-in helpers.
public sealed partial class FoodOrderApiTests
{
    private const string Operations = "/api/v1/admin/operations";

    [DatabaseFact]
    public async Task Only_administrators_can_see_what_needs_attention()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Operations)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, Operations, await SignInAsync(client))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, Operations, await StaffTokenAsync("support_agent"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, Operations, await StaffTokenAsync("content_editor"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, Operations, admin)).StatusCode);
    }

    [DatabaseFact]
    public async Task The_snapshot_reads_every_part_of_the_platform_from_PostgreSQL()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await SignInAsync(client);

        var snapshot = await ReadAsync(await SendAsync(client, HttpMethod.Get, Operations, admin));

        Assert.Equal(JsonValueKind.Array, snapshot.GetProperty("alerts").ValueKind);
        Assert.True(snapshot.GetProperty("reviews").GetProperty("pending").GetInt32() >= 0);
        var counts = snapshot.GetProperty("support").GetProperty("counts");
        foreach (var name in new[] { "needsReply", "waiting", "unassigned", "overdue", "escalated" })
        {
            Assert.True(counts.GetProperty(name).GetInt32() >= 0, name);
        }

        Assert.True(snapshot.GetProperty("foodOrdersNeedingAttention").GetInt32() >= 0);
        Assert.True(snapshot.GetProperty("openReconciliationExceptions").GetInt32() >= 0);
        Assert.True(snapshot.GetProperty("notifications").GetProperty("unsent").GetInt32() >= 0);
        var signIn = snapshot.GetProperty("signIn");
        Assert.True(signIn.GetProperty("codesLast24Hours").GetInt32() >= signIn.GetProperty("codesLastHour").GetInt32());
        Assert.True(signIn.GetProperty("codesLast24Hours").GetInt32() > 0, "signing in asks for a code");

        // The limiter's own counts are included, and say what kind of request this very call was.
        var classes = snapshot.GetProperty("rateLimits").GetProperty("classes").EnumerateArray().ToList();
        Assert.Contains(classes, c => c.GetProperty("name").GetString() == "staff" && c.GetProperty("requests").GetInt64() >= 1);
        Assert.Contains(classes, c => c.GetProperty("name").GetString() == "auth" && c.GetProperty("requests").GetInt64() >= 1);
        Assert.DoesNotContain("address", snapshot.GetProperty("rateLimits").GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
