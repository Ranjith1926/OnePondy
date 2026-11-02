using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace OnePondy.IntegrationTests;

// Cash on delivery (spec Phase 8, step 8.8), against a real PostgreSQL database: the HTTP flow from checkout to the cash a driver hands in,
// simultaneous deposits, and the rules only the database can enforce. Part of FoodOrderApiTests so these tests never run alongside the
// one that switches the food module off. The cash module is switched on for the length of a test and always switched back off.
public sealed partial class FoodOrderApiTests
{
    private const string CashModule = "/api/v1/admin/config/modules/cashOnDelivery";

    private async Task SetCashModuleAsync(HttpClient client, string admin, string status)
    {
        var response = await SendAsync(client, HttpMethod.Put, CashModule, admin, new { status });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Has the restaurant take cash on delivery (it opts in with its own settings, which carry the version it last saw).</summary>
    private async Task OptInAsync(HttpClient client, Shop shop, bool accepts = true)
    {
        var profile = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/restaurant", shop.Owner));
        var areas = profile.GetProperty("deliveryAreas").EnumerateArray().Select(a => a.GetString()).ToArray();
        await ReadAsync(await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/partner/businesses/{shop.BusinessId}/restaurant",
            shop.Owner,
            new
            {
                prepTimeMinutes = profile.GetProperty("prepTimeMinutes").GetInt32(),
                minOrderAmount = profile.GetProperty("minOrderAmount").GetDecimal(),
                isAcceptingOrders = profile.GetProperty("isAcceptingOrders").GetBoolean(),
                deliveryAreas = areas,
                version = VersionOf(profile),
                acceptsCashOnDelivery = accepts,
            }));
    }

    private static object CashOrderBody(Shop shop, string key) => new
    {
        idempotencyKey = key,
        businessId = shop.BusinessId,
        area = "whiteTown",
        address = Address,
        note = "Ring the bell",
        contactPhone = "+919876500001",
        lines = DefaultLines(shop),
        expectedTotal = 339.25m,
        paymentMethod = "cashOnDelivery",
    };

    /// <summary>Places a cash order and works it through the kitchen to ready. Returns the order id.</summary>
    private async Task<string> ReadyCashOrderAsync(HttpClient client, Shop shop, string customer)
    {
        var placed = await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, CashOrderBody(shop, Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        var order = await placed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cashOnDelivery", order.GetProperty("paymentMethod").GetString());
        var id = Id(order);
        var version = VersionOf(order);
        foreach (var step in new[] { "accept", "preparing", "ready" })
        {
            var moved = await ReadAsync(await SendAsync(client, HttpMethod.Post, PartnerPath(shop, id, step), shop.Owner, new { version }));
            version = VersionOf(moved);
        }

        return id;
    }

    private async Task<(string Held, string Collected)> DriverCashRowAsync(string userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var account = $"driver_cash:{userId}";
        var held = await DbOf(scope).LedgerEntries.AsNoTracking().Where(e => e.Account == account).SumAsync(e => e.Amount);
        var collected = await DbOf(scope).FoodOrders.AsNoTracking().CountAsync(o => o.CashCollectedBy == Guid.Parse(userId));
        return (held.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), collected.ToString());
    }

    [DatabaseFact]
    public async Task A_customer_pays_in_cash_at_the_door_and_the_driver_hands_the_cash_in()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        await SetCashModuleAsync(client, admin, "available");
        try
        {
            var shop = await OpenShopAsync(client, admin, UniqueName("Cash Corner"));
            var customer = await SignInAsync(client);
            var courier = await CourierAsync(client, admin);

            // Not offered until the restaurant opts in.
            var quoteBody = new { businessId = shop.BusinessId, area = "whiteTown", lines = DefaultLines(shop) };
            var before = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/food/quote", customer, quoteBody));
            Assert.False(before.GetProperty("paymentOptions").GetProperty("cashOnDelivery").GetBoolean());
            var refused = await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, CashOrderBody(shop, "cash-refused"));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("cash_on_delivery_unavailable", await ErrorCodeAsync(refused));

            await OptInAsync(client, shop);
            var after = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/food/quote", customer, quoteBody));
            Assert.True(after.GetProperty("paymentOptions").GetProperty("cashOnDelivery").GetBoolean());
            Assert.Equal(2000m, after.GetProperty("paymentOptions").GetProperty("cashOnDeliveryMaxAmount").GetDecimal());

            // The restaurant sees that it is a cash order, and accepting confirms it at once (nothing to pay online).
            var orderId = await ReadyCashOrderAsync(client, shop, customer);
            var inbox = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/orders?status=readyForPickup", shop.Owner));
            Assert.Equal("cashOnDelivery", inbox.EnumerateArray().Single(o => o.GetProperty("id").GetString() == orderId).GetProperty("paymentMethod").GetString());

            // The driver sees what they will collect, before and after taking it.
            var jobs = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/jobs", courier.Token));
            var offer = jobs.GetProperty("open").EnumerateArray().Single(o => o.GetProperty("restaurantName").GetString()!.StartsWith("Cash Corner"));
            Assert.Equal("cashOnDelivery", offer.GetProperty("paymentMethod").GetString());
            Assert.Equal(339.25m, offer.GetProperty("cashToCollect").GetDecimal());
            var jobId = offer.GetProperty("id").GetString()!;
            var taken = await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), courier.Token));
            Assert.Equal(339.25m, taken.GetProperty("cashToCollect").GetDecimal());
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "pickup"), courier.Token));

            // Handing over needs the exact cash confirmed.
            foreach (object? body in new object?[] { null, new { }, new { cashCollected = 300 }, new { cashCollected = 339.24 }, new { cashCollected = 400 } })
            {
                var short1 = await SendAsync(client, HttpMethod.Post, JobPath(jobId, "deliver"), courier.Token, body);
                Assert.Equal(HttpStatusCode.BadRequest, short1.StatusCode);
            }

            Assert.Equal("outForDelivery", (await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer))).GetProperty("status").GetString());
            var delivered = await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "deliver"), courier.Token, new { cashCollected = 339.25 }));
            Assert.Equal("delivered", delivered.GetProperty("status").GetString());

            // The customer's order shows the cash taken; the driver owes it.
            var done = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{orderId}", customer));
            Assert.Equal(339.25m, done.GetProperty("cashCollected").GetDecimal());
            Assert.Equal("cashOnDelivery", done.GetProperty("paymentMethod").GetString());
            Assert.Equal(JsonValueKind.Null, done.GetProperty("payment").ValueKind);
            var mine = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/cash", courier.Token));
            Assert.Equal(339.25m, mine.GetProperty("held").GetDecimal());
            Assert.Equal(5000m, mine.GetProperty("limit").GetDecimal());
            Assert.Equal("339.25", (await DriverCashRowAsync(courier.UserId)).Held);

            // An administrator sees who holds cash, and records what is handed in; it can never be more than they hold.
            var holding = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/cash/drivers", admin));
            var row = holding.EnumerateArray().Single(r => r.GetProperty("driverUserId").GetString() == courier.UserId);
            Assert.Equal(339.25m, row.GetProperty("held").GetDecimal());
            Assert.Equal("Ravi Kumar", row.GetProperty("driverName").GetString());

            var depositPath = $"/api/v1/admin/cash/drivers/{courier.UserId}/deposits";
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, depositPath, admin, new { amount = 400 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, depositPath, admin, new { amount = 0 })).StatusCode);
            var partial = await ReadAsync(await SendAsync(client, HttpMethod.Post, depositPath, admin, new { amount = 139.25, reference = "REC-1" }));
            Assert.Equal(200m, partial.GetProperty("held").GetDecimal());
            Assert.Equal(1, await AuditCountAsync("driver_cash.deposit_recorded", courier.UserId));
            Assert.Equal("200.00", (await DriverCashRowAsync(courier.UserId)).Held);

            var rest = await ReadAsync(await SendAsync(client, HttpMethod.Post, depositPath, admin, new { amount = 200 }));
            Assert.Equal(0m, rest.GetProperty("held").GetDecimal());
            var nothingLeft = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/cash/drivers", admin));
            Assert.DoesNotContain(nothingLeft.EnumerateArray(), r => r.GetProperty("driverUserId").GetString() == courier.UserId);
        }
        finally
        {
            await SetCashModuleAsync(client, admin, "comingSoon");
        }
    }

    [DatabaseFact]
    public async Task Two_deposits_for_the_same_cash_recorded_at_once_cannot_both_succeed()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        await SetCashModuleAsync(client, admin, "available");
        try
        {
            var shop = await OpenShopAsync(client, admin, UniqueName("Race Cash"));
            await OptInAsync(client, shop);
            var customer = await SignInAsync(client);
            var courier = await CourierAsync(client, admin);
            var orderId = await ReadyCashOrderAsync(client, shop, customer);
            var jobId = await JobIdOfAsync(orderId);
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), courier.Token));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "pickup"), courier.Token));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "deliver"), courier.Token, new { cashCollected = 339.25 }));
            var depositPath = $"/api/v1/admin/cash/drivers/{courier.UserId}/deposits";

            // The driver holds 339.25; two administrators each try to record all of it, at the same moment, several times over with fresh cash each round.
            for (var round = 0; round < 3; round++)
            {
                var held = decimal.Parse((await DriverCashRowAsync(courier.UserId)).Held, System.Globalization.CultureInfo.InvariantCulture);
                if (held == 0m)
                {
                    var next = await ReadyCashOrderAsync(client, shop, customer);
                    var nextJob = await JobIdOfAsync(next);
                    await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(nextJob, "accept"), courier.Token));
                    await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(nextJob, "pickup"), courier.Token));
                    await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(nextJob, "deliver"), courier.Token, new { cashCollected = 339.25 }));
                    held = 339.25m;
                }

                var responses = await Task.WhenAll(
                    SendAsync(client, HttpMethod.Post, depositPath, admin, new { amount = held }),
                    SendAsync(client, HttpMethod.Post, depositPath, admin, new { amount = held }));

                Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
                var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
                Assert.True(loser.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict, $"{loser.StatusCode}");
                Assert.Equal("0.00", (await DriverCashRowAsync(courier.UserId)).Held);
            }
        }
        finally
        {
            await SetCashModuleAsync(client, admin, "comingSoon");
        }
    }

    [DatabaseFact]
    public async Task Cash_on_delivery_is_for_administrators_to_switch_on_and_the_cash_book_is_theirs_alone()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var customer = await SignInAsync(client);
        var courier = await CourierAsync(client, admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Put, CashModule, customer, new { status = "available" })).StatusCode);
        foreach (var path in new[] { "/api/v1/admin/cash/drivers", $"/api/v1/admin/cash/drivers/{courier.UserId}/deposits" })
        {
            var method = path.EndsWith("deposits", StringComparison.Ordinal) ? HttpMethod.Post : HttpMethod.Get;
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, method, path, null, new { amount = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, method, path, customer, new { amount = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, method, path, courier.Token, new { amount = 1 })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/driver/cash", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/cash/drivers/{Guid.NewGuid()}/deposits", admin, new { amount = 1 })).StatusCode);

        // The module starts off, and the checkout says so: nothing offers cash until an administrator has switched it on.
        var shop = await OpenShopAsync(client, admin, UniqueName("Module off"));
        await TurnFoodOnAsync(client, admin);
        await OptInAsync(client, shop);
        var options = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/food/quote", customer, new { businessId = shop.BusinessId, area = "whiteTown", lines = DefaultLines(shop) }));
        Assert.False(options.GetProperty("paymentOptions").GetProperty("cashOnDelivery").GetBoolean());
        Assert.Equal("cash_on_delivery_unavailable", await ErrorCodeAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, CashOrderBody(shop, "module-off"))));
    }

    [DatabaseFact]
    public async Task The_database_keeps_cash_honest()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        await SetCashModuleAsync(client, admin, "available");
        try
        {
            var shop = await OpenShopAsync(client, admin, UniqueName("Honest Cash"));
            await OptInAsync(client, shop);
            var customer = await SignInAsync(client);
            var courier = await CourierAsync(client, admin);
            var cashOrder = Guid.Parse(await ReadyCashOrderAsync(client, shop, customer));
            var jobId = await JobIdOfAsync(cashOrder.ToString());
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "accept"), courier.Token));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "pickup"), courier.Token));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, JobPath(jobId, "deliver"), courier.Token, new { cashCollected = 339.25 }));
            var onlineOrder = Guid.Parse(Id(await PlaceAsync(client, customer, shop)));
            var driver = Guid.Parse(courier.UserId);
            await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/cash/drivers/{courier.UserId}/deposits", admin, new { amount = 100 }));

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

            // The cash on a delivered cash order cannot be erased or changed, and an order paid online cannot carry any.
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE food_orders SET cash_collected = NULL, cash_collected_at = NULL, cash_collected_by = NULL WHERE id = @id", ("id", cashOrder))).SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE food_orders SET cash_collected = 1 WHERE id = @id", ("id", cashOrder))).SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE food_orders SET cash_collected = total_amount, cash_collected_at = now(), cash_collected_by = customer_user_id WHERE id = @id", ("id", onlineOrder))).SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE food_orders SET payment_method = 'barter' WHERE id = @id", ("id", onlineOrder))).SqlState);
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE food_orders SET payment_method = 'cash_on_delivery', status = 'delivered', expires_at = NULL WHERE id = @id", ("id", onlineOrder))).SqlState);

            // A recorded deposit is never edited or removed, and has to make sense.
            Assert.Equal("P0001", (await RefusedAsync("UPDATE driver_cash_deposits SET amount = 1 WHERE driver_user_id = @id", ("id", driver))).SqlState);
            Assert.Equal("P0001", (await RefusedAsync("DELETE FROM driver_cash_deposits WHERE driver_user_id = @id", ("id", driver))).SqlState);
            Assert.Equal("P0001", (await RefusedAsync("TRUNCATE driver_cash_deposits")).SqlState);
            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                (await RefusedAsync("INSERT INTO driver_cash_deposits (id, driver_user_id, amount, received_at, recorded_by_user_id, recorded_at) VALUES (gen_random_uuid(), @id, 0, now(), @id, now())", ("id", driver))).SqlState);
            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                (await RefusedAsync("INSERT INTO driver_cash_deposits (id, driver_user_id, amount, received_at, recorded_by_user_id, recorded_at) VALUES (gen_random_uuid(), @id, 5, now() + interval '1 day', @id, now())", ("id", driver))).SqlState);

            // A ledger account has to be a real one.
            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                (await RefusedAsync("INSERT INTO ledger_entries (id, transaction_id, account, amount, description, created_at) VALUES (gen_random_uuid(), gen_random_uuid(), 'driver_cash:oops', 5, 'x', now())")).SqlState);

            // A driver's cash cannot go below nothing, even by a write that skips the application: the driver holds 239.25 now, and moving
            // 1000 from their account to the cash received is balanced, but refused when it commits.
            await using var transaction = await sql.BeginTransactionAsync();
            await using (var insert = sql.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO ledger_entries (id, transaction_id, account, amount, description, created_at) VALUES " +
                    "(gen_random_uuid(), @t, 'cash_received', 1000, 'overdrawn', now()), " +
                    "(gen_random_uuid(), @t, 'driver_cash:' || @driver, -1000, 'overdrawn', now())";
                insert.Parameters.AddWithValue("t", Guid.NewGuid());
                insert.Parameters.AddWithValue("driver", driver.ToString());
                Assert.Equal(2, await insert.ExecuteNonQueryAsync());
            }

            var overdrawn = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            Assert.Equal("P0001", overdrawn.SqlState);
        }
        finally
        {
            await SetCashModuleAsync(client, admin, "comingSoon");
        }
    }
}
