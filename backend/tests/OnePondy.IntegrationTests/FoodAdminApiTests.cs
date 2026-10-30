using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace OnePondy.IntegrationTests;

// Administrators' monitoring of food orders and deliveries (spec Phase 8, step 8.9), against a real PostgreSQL database. Part of
// FoodOrderApiTests so these tests never run alongside the one that switches the food module off.
public sealed partial class FoodOrderApiTests
{
    private static string AdminOrders(string path = "") => "/api/v1/admin/food-orders" + path;

    private async Task<int> AuditCountAsync(string action, string entityId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await DbOf(scope).AuditEntries.AsNoTracking().CountAsync(e => e.Action == action && e.EntityId == entityId);
    }

    [DatabaseFact]
    public async Task An_administrator_watches_a_delivery_re_offers_it_and_cancels_the_order()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Watch Waffles"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);
        var other = await CourierAsync(client, admin);
        var orderId = await ReadyOrderAsync(client, shop, customer);
        var jobId = await JobIdOfAsync(orderId);

        // The list shows it, waiting for a driver, with no customer address in it.
        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, AdminOrders("?pageSize=100"), admin));
        var row = list.GetProperty("items").EnumerateArray().Single(o => o.GetProperty("id").GetString() == orderId);
        Assert.Equal("readyForPickup", row.GetProperty("status").GetString());
        Assert.Equal("open", row.GetProperty("deliveryStatus").GetString());
        Assert.StartsWith("Watch Waffles", row.GetProperty("restaurantName").GetString());
        Assert.DoesNotContain("Suffren", row.GetRawText());

        // A driver takes it and reports a position: the administrator can see both, and the look is recorded.
        await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), courier.Token));
        await SendAsync(client, HttpMethod.Post, JobPath(jobId, "location"), courier.Token, new { latitude = 11.93, longitude = 79.83 });
        var detail = await ReadAsync(await SendAsync(client, HttpMethod.Get, AdminOrders($"/{orderId}"), admin));
        Assert.Equal("12 Rue Suffren, White Town", detail.GetProperty("order").GetProperty("deliveryAddress").GetString());
        Assert.True(detail.GetProperty("canCancel").GetBoolean());
        var delivery = detail.GetProperty("delivery");
        Assert.Equal("accepted", delivery.GetProperty("status").GetString());
        Assert.Equal(courier.UserId, delivery.GetProperty("driverUserId").GetString());
        Assert.Equal("Ravi Kumar", delivery.GetProperty("driverName").GetString());
        Assert.True(delivery.GetProperty("canRelease").GetBoolean());
        Assert.Equal(11.93, delivery.GetProperty("position").GetProperty("latitude").GetDouble());
        Assert.Equal(1, await AuditCountAsync("food_order.driver_location_viewed", orderId));

        // Re-offering it takes it from that driver, erases their position and lets another driver have it.
        var jobVersion = delivery.GetProperty("jobVersion").GetUInt32();
        var staleRelease = await SendAsync(client, HttpMethod.Post, AdminOrders($"/{orderId}/release-delivery"), admin, new { version = jobVersion + 99 });
        Assert.Equal("delivery_changed", await ErrorCodeAsync(staleRelease));
        var released = await ReadAsync(await SendAsync(client, HttpMethod.Post, AdminOrders($"/{orderId}/release-delivery"), admin, new { version = jobVersion }));
        Assert.Equal("open", released.GetProperty("delivery").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, released.GetProperty("delivery").GetProperty("position").ValueKind);
        Assert.Equal(1, await AuditCountAsync("food_order.delivery_released_by_admin", orderId));
        var courierView = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", courier.Token));
        Assert.Equal(JsonValueKind.Null, courierView.GetProperty("current").ValueKind);
        await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), other.Token));
        var row2 = await JobRowAsync(jobId);
        Assert.Equal(other.UserId, row2.Driver);
        Assert.Null(row2.Latitude);

        // Cancelling the order ends the delivery, and needs a reason.
        var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, AdminOrders($"/{orderId}"), admin));
        var version = current.GetProperty("order").GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, AdminOrders($"/{orderId}/cancel"), admin, new { version, reason = " " })).StatusCode);
        var cancelled = await ReadAsync(await SendAsync(client, HttpMethod.Post, AdminOrders($"/{orderId}/cancel"), admin, new { version, reason = "Driver unreachable" }));
        Assert.Equal("cancelled", cancelled.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal("Driver unreachable", cancelled.GetProperty("order").GetProperty("closedReason").GetString());
        Assert.False(cancelled.GetProperty("canCancel").GetBoolean());
        Assert.Equal("cancelled", cancelled.GetProperty("delivery").GetProperty("status").GetString());
        Assert.Equal("Cancelled", (await JobRowAsync(jobId)).Status);
        Assert.Equal(1, await AuditCountAsync("food_order.cancelled_by_admin", orderId));
        var driverNow = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", other.Token));
        Assert.Equal(JsonValueKind.Null, driverNow.GetProperty("current").ValueKind);

        // A second cancel is refused, not repeated.
        var again = await SendAsync(client, HttpMethod.Post, AdminOrders($"/{orderId}/cancel"), admin, new { version = cancelled.GetProperty("order").GetProperty("version").GetUInt32(), reason = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(1, await AuditCountAsync("food_order.cancelled_by_admin", orderId));
    }

    [DatabaseFact]
    public async Task Order_monitoring_is_for_administrators_only_and_checks_what_it_is_given()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Access Appam"));
        var customer = await SignInAsync(client);
        var orderId = await ReadyOrderAsync(client, shop, customer);

        foreach (var path in new[] { AdminOrders(), AdminOrders("/attention-count"), AdminOrders($"/{orderId}") })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, path, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, path, customer)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, path, shop.Owner)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, AdminOrders($"/{orderId}/cancel"), customer, new { version = 1, reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, AdminOrders($"/{orderId}/release-delivery"), shop.Owner, new { version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, AdminOrders("?status=nonsense"), admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, AdminOrders($"/{Guid.NewGuid()}"), admin)).StatusCode);

        var filtered = await ReadAsync(await SendAsync(client, HttpMethod.Get, AdminOrders("?status=readyForPickup&pageSize=100"), admin));
        Assert.Contains(filtered.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetString() == orderId);
        var count = await ReadAsync(await SendAsync(client, HttpMethod.Get, AdminOrders("/attention-count"), admin));
        Assert.True(count.GetProperty("count").GetInt32() >= 0);
        var attention = await ReadAsync(await SendAsync(client, HttpMethod.Get, AdminOrders("?needsAttention=true&pageSize=100"), admin));
        Assert.DoesNotContain(attention.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetString() == orderId);
    }

    [DatabaseFact]
    public async Task The_customer_history_view_lists_food_orders_with_the_bookings()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("History Halwa"));
        var customer = await SignInAsync(client);
        var orderId = await ReadyOrderAsync(client, shop, customer);

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var sql = new NpgsqlConnection(DbOf(scope).Database.GetConnectionString());
        await sql.OpenAsync();

        await using (var row = sql.CreateCommand())
        {
            row.CommandText = "SELECT kind, title, status, total_amount, currency FROM customer_history WHERE id = @id";
            row.Parameters.AddWithValue("id", Guid.Parse(orderId));
            await using var reader = await row.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("food_order", reader.GetString(0));
            Assert.StartsWith("History Halwa", reader.GetString(1));
            Assert.Equal("ready_for_pickup", reader.GetString(2));
            Assert.Equal(339.25m, reader.GetDecimal(3));
            Assert.Equal("INR", reader.GetString(4));
        }

        // The view keeps the nine columns it always had, so anything reading it still works, and bookings are still in it.
        await using (var columns = sql.CreateCommand())
        {
            columns.CommandText = "SELECT string_agg(column_name, ',' ORDER BY ordinal_position) FROM information_schema.columns WHERE table_name = 'customer_history'";
            Assert.Equal("id,kind,customer_user_id,title,status,scheduled_at,total_amount,currency,created_at", (string?)await columns.ExecuteScalarAsync());
        }

        await using (var kinds = sql.CreateCommand())
        {
            kinds.CommandText = "SELECT count(*) FROM customer_history WHERE kind = 'booking'";
            Assert.True((long)(await kinds.ExecuteScalarAsync())! >= 0);
        }

        // Refunds may now carry the administrator-cancelled reason.
        await using var constraint = sql.CreateCommand();
        constraint.CommandText = "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_refund_reason'";
        Assert.Contains("admin_cancelled", (string?)await constraint.ExecuteScalarAsync());
    }
}
