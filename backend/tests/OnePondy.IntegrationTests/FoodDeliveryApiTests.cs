using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace OnePondy.IntegrationTests;

// Delivering food orders (spec Phase 8, step 8.7), against a real PostgreSQL database: the drivers' HTTP flow, the race for one job, the
// one-job-per-driver rule, cancellation, and the rules only the database can enforce. Part of FoodOrderApiTests so these tests never run
// alongside the one that switches the food module off.
public sealed partial class FoodOrderApiTests
{
    private sealed record Courier(string Token, string UserId);

    private static string JobPath(string jobId, string action) => $"/api/v1/driver/jobs/{jobId}/{action}";

    /// <summary>An approved driver on a bicycle, already on duty.</summary>
    private async Task<Courier> CourierAsync(HttpClient client, string admin)
    {
        var token = await SignInAsync(client);
        var applied = await ReadAsync(await SendAsync(
            client, HttpMethod.Put, "/api/v1/driver/me", token, new { fullName = "Ravi Kumar", vehicleType = "bicycle" }));
        var userId = applied.GetProperty("userId").GetString()!;
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/approve", admin, new { version = VersionOf(applied) }));
        var approved = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", token));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = true, version = VersionOf(approved) }));
        return new Courier(token, userId);
    }

    /// <summary>Places an order and works it through the kitchen to ready, so a delivery job is open. Returns the order id.</summary>
    private async Task<string> ReadyOrderAsync(HttpClient client, Shop shop, string customer)
    {
        var order = await PlaceAsync(client, customer, shop);
        var id = Id(order);
        var version = VersionOf(order);
        foreach (var step in new[] { "accept", "preparing", "ready" })
        {
            var moved = await ReadAsync(await SendAsync(client, HttpMethod.Post, PartnerPath(shop, id, step), shop.Owner, new { version }));
            version = VersionOf(moved);
        }

        return id;
    }

    private async Task<string> JobIdOfAsync(string orderId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return (await DbOf(scope).DeliveryJobs.AsNoTracking().SingleAsync(j => j.FoodOrderId == Guid.Parse(orderId))).Id.ToString();
    }

    private async Task<(string Status, string? Driver, double? Latitude, DateTimeOffset? LocationAt)> JobRowAsync(string jobId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var job = await DbOf(scope).DeliveryJobs.AsNoTracking().SingleAsync(j => j.Id == Guid.Parse(jobId));
        return (job.Status.ToString(), job.DriverUserId?.ToString(), job.LastLatitude, job.LastLocationAt);
    }

    [DatabaseFact]
    public async Task A_driver_takes_collects_and_delivers_an_order_and_the_customer_follows_it()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Courier Cafe"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);
        var orderId = await ReadyOrderAsync(client, shop, customer);

        // The driver sees it as an offer: restaurant and area, but not where the customer lives.
        var jobs = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", courier.Token));
        Assert.True(jobs.GetProperty("canAcceptJobs").GetBoolean());
        var offer = jobs.GetProperty("open").EnumerateArray().Single(o => o.GetProperty("restaurantName").GetString()!.StartsWith("Courier Cafe"));
        Assert.DoesNotContain("Suffren", jobs.GetRawText());
        Assert.DoesNotContain("9876500001", jobs.GetRawText());
        var jobId = offer.GetProperty("id").GetString()!;
        Assert.Equal(jobId, await JobIdOfAsync(orderId));

        // The restaurant and the customer see it waiting, then who is coming.
        var waiting = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));
        Assert.Equal("open", waiting.GetProperty("delivery").GetProperty("status").GetString());

        // The restaurant has told us where it is, so the driver gets the pickup point for a map (never the customer's address).
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await DbOf(scope).Businesses.Where(b => b.Id == Guid.Parse(shop.BusinessId))
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.Latitude, 11.9416).SetProperty(b => b.Longitude, 79.8083));
        }

        var accepted = await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), courier.Token));
        Assert.Equal("accepted", accepted.GetProperty("status").GetString());
        Assert.Equal("12 Rue Suffren, White Town", accepted.GetProperty("deliveryAddress").GetString());
        Assert.Equal(11.9416, accepted.GetProperty("restaurantLatitude").GetDouble());
        Assert.Equal(79.8083, accepted.GetProperty("restaurantLongitude").GetDouble());
        Assert.Equal("+919876500001", accepted.GetProperty("contactPhone").GetString());

        var inbox = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/orders?status=readyForPickup", shop.Owner));
        var row = inbox.EnumerateArray().Single(o => o.GetProperty("id").GetString() == orderId);
        Assert.Equal("accepted", row.GetProperty("delivery").GetProperty("status").GetString());
        Assert.Equal("Ravi", row.GetProperty("delivery").GetProperty("driverFirstName").GetString());
        Assert.Equal("bicycle", row.GetProperty("delivery").GetProperty("vehicleType").GetString());
        Assert.DoesNotContain("Suffren", row.GetRawText());

        // The driver reports their position while heading out to the restaurant; the customer sees only how fresh it is, not the coordinates.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, JobPath(jobId, "location"), courier.Token, new { latitude = 11.93, longitude = 79.83 })).StatusCode);
        var held = await JobRowAsync(jobId);
        Assert.Equal(11.93, held.Latitude);
        var heading = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));
        Assert.DoesNotContain("11.93", heading.GetRawText());
        Assert.DoesNotContain("79.83", heading.GetRawText());

        var picked = await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "pickup"), courier.Token));
        Assert.Equal("pickedUp", picked.GetProperty("status").GetString());

        // Once the driver is carrying the food, the customer who ordered (and only they) can see where the driver is.
        var onTheWay = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));
        Assert.Equal("outForDelivery", onTheWay.GetProperty("status").GetString());
        var delivery = onTheWay.GetProperty("delivery");
        Assert.Equal("pickedUp", delivery.GetProperty("status").GetString());
        Assert.Equal("Ravi", delivery.GetProperty("driverFirstName").GetString());
        Assert.NotEqual(JsonValueKind.Null, delivery.GetProperty("locationUpdatedAt").ValueKind);
        Assert.Equal(11.93, delivery.GetProperty("position").GetProperty("latitude").GetDouble());
        Assert.Equal(79.83, delivery.GetProperty("position").GetProperty("longitude").GetDouble());

        // The restaurant never gets the coordinates, even now.
        var restaurantView = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/orders/{orderId}", shop.Owner));
        Assert.DoesNotContain("11.93", restaurantView.GetRawText());
        Assert.DoesNotContain("79.83", restaurantView.GetRawText());

        var delivered = await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "deliver"), courier.Token));
        Assert.Equal("delivered", delivered.GetProperty("status").GetString());

        var done = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));
        Assert.Equal("delivered", done.GetProperty("status").GetString());
        Assert.Equal("driver", done.GetProperty("history").EnumerateArray().Last().GetProperty("actorType").GetString());
        Assert.True(!done.GetProperty("delivery").TryGetProperty("position", out var gone) || gone.ValueKind == JsonValueKind.Null);

        // Nothing about where the driver was is kept once the delivery is over.
        var finished = await JobRowAsync(jobId);
        Assert.Equal("Delivered", finished.Status);
        Assert.Null(finished.Latitude);
        Assert.Null(finished.LocationAt);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, JobPath(jobId, "location"), courier.Token, new { latitude = 11.9, longitude = 79.8 })).StatusCode);

        // And the driver is free for the next one.
        var after = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", courier.Token));
        Assert.Equal(JsonValueKind.Null, after.GetProperty("current").ValueKind);
    }

    [DatabaseFact]
    public async Task Drivers_accepting_the_same_job_at_once_never_both_get_it()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Race Rolls"));
        var customer = await SignInAsync(client);
        var drivers = new List<Courier>();
        for (var i = 0; i < 4; i++)
        {
            drivers.Add(await CourierAsync(client, admin));
        }

        // Five rounds, each with a fresh job and four drivers pressing "accept" at the same moment.
        for (var round = 0; round < 5; round++)
        {
            var orderId = await ReadyOrderAsync(client, shop, customer);
            var jobId = await JobIdOfAsync(orderId);

            var responses = await Task.WhenAll(drivers.Select(d => SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), d.Token)));

            var winners = drivers.Where((_, i) => responses[i].StatusCode == HttpStatusCode.OK).ToList();
            var winner = Assert.Single(winners);
            foreach (var (response, driver) in responses.Zip(drivers).Where(p => p.Second != winner))
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("job_taken", await ErrorCodeAsync(response));
            }

            var row = await JobRowAsync(jobId);
            Assert.Equal("Accepted", row.Status);
            Assert.Equal(winner.UserId, row.Driver);

            // The winner finishes, so the next round starts with everyone free.
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "pickup"), winner.Token));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "deliver"), winner.Token));
        }
    }

    [DatabaseFact]
    public async Task One_driver_accepting_two_jobs_at_once_gets_exactly_one()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Double Dosa"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);

        for (var round = 0; round < 3; round++)
        {
            var first = await JobIdOfAsync(await ReadyOrderAsync(client, shop, customer));
            var second = await JobIdOfAsync(await ReadyOrderAsync(client, shop, customer));

            var responses = await Task.WhenAll(
                SendAsync(client, HttpMethod.Post, JobPath(first, "accept"), courier.Token),
                SendAsync(client, HttpMethod.Post, JobPath(second, "accept"), courier.Token));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            var refused = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("driver_has_active_job", await ErrorCodeAsync(refused));

            // Finish the one they got and give the other back to the pool so the next round is clean.
            var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", courier.Token));
            var held = current.GetProperty("current").GetProperty("id").GetString()!;
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(held, "pickup"), courier.Token));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(held, "deliver"), courier.Token));
            var leftover = held == first ? second : first;
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(leftover, "accept"), courier.Token));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(leftover, "pickup"), courier.Token));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(leftover, "deliver"), courier.Token));
        }
    }

    [DatabaseFact]
    public async Task A_cancelled_order_takes_its_delivery_with_it()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Cancel Chaat"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);
        var other = await CourierAsync(client, admin);
        var orderId = await ReadyOrderAsync(client, shop, customer);
        var jobId = await JobIdOfAsync(orderId);
        await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), courier.Token));
        await SendAsync(client, HttpMethod.Post, JobPath(jobId, "location"), courier.Token, new { latitude = 11.93, longitude = 79.83 });

        var order = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/orders/{orderId}", shop.Owner));
        var cancelled = await SendAsync(client, HttpMethod.Post, PartnerPath(shop, orderId, "cancel"), shop.Owner, new { version = VersionOf(order), reason = "Out of gas" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var row = await JobRowAsync(jobId);
        Assert.Equal("Cancelled", row.Status);
        Assert.Null(row.Latitude);
        var view = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", courier.Token));
        Assert.Equal(JsonValueKind.Null, view.GetProperty("current").ValueKind);
        Assert.Equal("job_invalid_transition", await ErrorCodeAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "pickup"), courier.Token)));
        Assert.Equal("job_taken", await ErrorCodeAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), other.Token)));
        var customerView = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));
        Assert.Equal(JsonValueKind.Null, customerView.GetProperty("delivery").ValueKind);
    }

    [DatabaseFact]
    public async Task Only_approved_drivers_on_duty_can_take_jobs_and_only_the_holder_can_act_on_one()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Access Appam"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);
        var stranger = await SignInAsync(client);
        var orderId = await ReadyOrderAsync(client, shop, customer);
        var jobId = await JobIdOfAsync(orderId);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), null)).StatusCode);

        // Someone who never applied sees no jobs and cannot take one.
        var none = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", stranger));
        Assert.False(none.GetProperty("canAcceptJobs").GetBoolean());
        Assert.Empty(none.GetProperty("open").EnumerateArray());
        Assert.Equal("driver_not_available", await ErrorCodeAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), stranger)));

        // A customer cannot move the delivery either, and a driver who does not hold it gets "not found".
        await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), courier.Token));
        foreach (var action in new[] { "pickup", "deliver", "release" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, JobPath(jobId, action), stranger)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, JobPath(jobId, action), customer)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, JobPath(jobId, "location"), courier.Token, new { latitude = 123.0, longitude = 79.83 })).StatusCode);
        Assert.Equal("accepted", (await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", courier.Token))).GetProperty("current").GetProperty("status").GetString());
    }

    [DatabaseFact]
    public async Task The_database_keeps_deliveries_honest()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Honest Idli"));
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);
        var rival = await CourierAsync(client, admin);
        var taken = await JobIdOfAsync(await ReadyOrderAsync(client, shop, customer));
        var open = await JobIdOfAsync(await ReadyOrderAsync(client, shop, customer));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(taken, "accept"), courier.Token));

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var sql = new NpgsqlConnection(DbOf(scope).Database.GetConnectionString());
        await sql.OpenAsync();

        async Task<PostgresException> RefusedAsync(string text, params (string Name, object Value)[] parameters)
        {
            await using var command = sql.CreateCommand();
            command.CommandText = text;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        var takenId = Guid.Parse(taken);
        var openId = Guid.Parse(open);
        var rivalId = Guid.Parse(rival.UserId);

        // A job that is taken cannot be handed to a second driver, even by a write that skips the application: this is the rule that
        // makes "exactly one driver gets it" true however the accepts arrive.
        Assert.Equal("P0001", (await RefusedAsync("UPDATE delivery_jobs SET driver_user_id = @d WHERE id = @id", ("d", rivalId), ("id", takenId))).SqlState);

        // Steps only go forward: an open job cannot jump to picked up or delivered, a finished job cannot change, and none can be deleted.
        Assert.Equal("P0001", (await RefusedAsync("UPDATE delivery_jobs SET status = 'delivered' WHERE id = @id", ("id", openId))).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("UPDATE delivery_jobs SET status = 'picked_up' WHERE id = @id", ("id", openId))).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("DELETE FROM delivery_jobs WHERE id = @id", ("id", openId))).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("TRUNCATE delivery_jobs")).SqlState);

        // One active job per driver, however it is written.
        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            (await RefusedAsync("UPDATE delivery_jobs SET status = 'accepted', driver_user_id = @d, accepted_at = now() WHERE id = @id", ("d", Guid.Parse(courier.UserId)), ("id", openId))).SqlState);

        // The shape of a job: open means nobody has it, a taken one says who, and the driver's position exists only during the delivery.
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE delivery_jobs SET driver_user_id = @d WHERE id = @id", ("d", rivalId), ("id", openId))).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("UPDATE delivery_jobs SET driver_user_id = NULL WHERE id = @id", ("id", takenId))).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE delivery_jobs SET last_latitude = 11, last_longitude = 79, last_location_at = now() WHERE id = @id", ("id", openId))).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE delivery_jobs SET last_latitude = 11 WHERE id = @id", ("id", takenId))).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE delivery_jobs SET last_latitude = 95, last_longitude = 79, last_location_at = now() WHERE id = @id", ("id", takenId))).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("UPDATE delivery_jobs SET status = 'teleported' WHERE id = @id", ("id", openId))).SqlState);

        // One delivery per order.
        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            (await RefusedAsync(
                "INSERT INTO delivery_jobs (id, food_order_id, status, opened_at, updated_at) SELECT gen_random_uuid(), food_order_id, 'open', now(), now() FROM delivery_jobs WHERE id = @id",
                ("id", takenId))).SqlState);

        // A finished job is frozen.
        await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(taken, "pickup"), courier.Token));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(taken, "deliver"), courier.Token));
        Assert.Equal("P0001", (await RefusedAsync("UPDATE delivery_jobs SET status = 'cancelled', cancelled_at = now() WHERE id = @id", ("id", takenId))).SqlState);
    }
}
