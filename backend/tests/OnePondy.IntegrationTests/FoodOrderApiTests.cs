using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Food orders (spec Phase 8, step 8.2), against a real, migrated PostgreSQL database: the HTTP flow from quote to a finished
/// kitchen, access control, simultaneous requests, and the rules only the database can enforce. Paying for orders (step 8.3) is in
/// FoodOrderPaymentApiTests.cs, part of this same class so its tests never run alongside the one that switches the food module off.
/// Runs only when ONEPONDY_TEST_DATABASE is set (see README).
/// </summary>
public sealed partial class FoodOrderApiTests : IAsyncLifetime
{
    private const string Address = "12 Rue Suffren, White Town";

    private ApiFactory? _factory;

    private ApiFactory Factory => _factory ?? throw new InvalidOperationException("No test database configured.");

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(DatabaseFactAttribute.VariableName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        _factory = new ApiFactory(connectionString, new Dictionary<string, string>
        {
            ["Auth:Otp:ResendInterval"] = "00:00:00",
            ["Auth:Otp:MaxPerDestination"] = "100",
        });
        await TestDatabase.MigrateOnceAsync(_factory.Services);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    // --- Helpers ---

    private async Task<string> AdministratorTokenAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountStore>();
        var staff = scope.ServiceProvider.GetRequiredService<OnePondy.Application.Staff.IStaffDirectory>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

        var account = await accounts.FindOrCreateAsync(OtpChannel.Email, ApiFactory.NewEmail("admin"), default);
        await staff.SetRoleAsync(account.Id, RoleNames.Administrator, default);
        var withRole = await accounts.FindActiveAsync(account.Id, default) ?? account;
        var tokens = await auth.StartSessionAsync(withRole, "integration-test", default);
        return tokens.AccessToken.Value;
    }

    private async Task<string> SignInAsync(HttpClient client)
    {
        var phone = ApiFactory.NewIndianMobile();
        var requested = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = phone });
        var challengeId = (await ReadAsync(requested)).GetProperty("challengeId").GetString();
        var verified = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId, code = Factory.Codes.For("+91" + phone) });
        return (await ReadAsync(verified)).GetProperty("accessToken").GetString()!;
    }

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid().ToString("N")[..8]}";

    private static OnePondyDbContext DbOf(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Food ordering is off until an administrator switches it on, as it will be in any real environment.</summary>
    private async Task TurnFoodOnAsync(HttpClient client, string admin)
    {
        var response = await SendAsync(client, HttpMethod.Put, "/api/v1/admin/config/modules/food", admin, new { status = "available" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private sealed record Shop(string Owner, string BusinessId, string Dosa, string Coffee, string Category, uint DosaVersion);

    /// <summary>An approved, open restaurant on White Town with a ₹120 dosa and a ₹45 coffee, smallest order ₹100.</summary>
    private async Task<Shop> OpenShopAsync(HttpClient client, string admin, string name)
    {
        var owner = await SignInAsync(client);
        var applied = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/partner/businesses",
            owner,
            new
            {
                name,
                businessType = "restaurant",
                description = "Home-style South Indian food.",
                phoneNumber = "9876543210",
                email = "owner@example.com",
                address = "White Town",
                lat = 11.93,
                lng = 79.83,
                gstin = (string?)null,
                fssaiLicenseNumber = "12345678901234",
            }));
        var businessId = applied.GetProperty("id").GetString()!;
        var approved = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{businessId}/approve", admin, new { version = applied.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var category = (await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/partner/businesses/{businessId}/menu/categories", owner, new { name = "Breakfast" }))).GetProperty("id").GetString()!;
        var dosa = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/partner/businesses/{businessId}/menu/items", owner, new { categoryId = category, name = "Masala dosa", price = 120, isVeg = true }));
        var coffee = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/partner/businesses/{businessId}/menu/items", owner, new { categoryId = category, name = "Filter coffee", price = 45, isVeg = true }));
        await ReadAsync(await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/partner/businesses/{businessId}/restaurant",
            owner,
            new { prepTimeMinutes = 25, minOrderAmount = 100, isAcceptingOrders = true, deliveryAreas = new[] { "whiteTown" } }));
        return new Shop(owner, businessId, dosa.GetProperty("id").GetString()!, coffee.GetProperty("id").GetString()!, category, dosa.GetProperty("version").GetUInt32());
    }

    private static object[] DefaultLines(Shop shop) =>
        [new { menuItemId = shop.Dosa, quantity = 2 }, new { menuItemId = shop.Coffee, quantity = 1 }];

    private static object OrderBody(Shop shop, string key, decimal expectedTotal = 339.25m) => new
    {
        idempotencyKey = key,
        businessId = shop.BusinessId,
        area = "whiteTown",
        address = Address,
        note = "Ring the bell",
        contactPhone = "+919876500001",
        lines = DefaultLines(shop),
        expectedTotal,
    };

    private async Task<JsonElement> PlaceAsync(HttpClient client, string customer, Shop shop, string? key = null) =>
        await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, OrderBody(shop, key ?? Guid.NewGuid().ToString("N"))));

    private static string Id(JsonElement order) => order.GetProperty("id").GetString()!;

    private static uint VersionOf(JsonElement order) => order.GetProperty("version").GetUInt32();

    private static string PartnerPath(Shop shop, string id, string action) => $"/api/v1/partner/businesses/{shop.BusinessId}/orders/{id}/{action}";

    // --- Tests ---

    [DatabaseFact]
    public async Task A_customer_prices_a_cart_places_an_order_and_the_kitchen_works_it_to_ready()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Spice Route"));
        var customer = await SignInAsync(client);

        // The quote shows the whole breakdown, and the prices come from the menu.
        var quote = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/food/quote", customer, new { businessId = shop.BusinessId, area = "whiteTown", lines = DefaultLines(shop) }));
        Assert.Equal(285m, quote.GetProperty("price").GetProperty("subtotal").GetDecimal());
        Assert.Equal(14.25m, quote.GetProperty("price").GetProperty("taxAmount").GetDecimal());
        Assert.Equal(339.25m, quote.GetProperty("price").GetProperty("totalAmount").GetDecimal());

        // Placing it: created, waiting for the restaurant, with the same total.
        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, OrderBody(shop, "flow-1"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("placed", order.GetProperty("status").GetString());
        Assert.True(order.GetProperty("canCancel").GetBoolean());
        Assert.Equal(339.25m, order.GetProperty("price").GetProperty("totalAmount").GetDecimal());
        Assert.Equal("12345678901234", order.GetProperty("fssaiLicenseNumber").GetString());
        var id = Id(order);

        // The restaurant's inbox shows it, without the customer's address or phone.
        var inbox = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/orders?status=placed", shop.Owner));
        var inboxRow = inbox.EnumerateArray().Single(o => o.GetProperty("id").GetString() == id);
        Assert.Equal(VersionOf(order), inboxRow.GetProperty("version").GetUInt32());
        Assert.Equal(["Filter coffee", "Masala dosa"], inboxRow.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
        Assert.DoesNotContain("Suffren", inboxRow.GetRawText());
        var asRestaurant = await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/orders/{id}", shop.Owner);
        var restaurantJson = await asRestaurant.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Suffren", restaurantJson);
        Assert.DoesNotContain("9876500001", restaurantJson);

        // Accept (online payment is off in this test, so it is confirmed), then cook.
        var accepted = await ReadAsync(await SendAsync(client, HttpMethod.Post, PartnerPath(shop, id, "accept"), shop.Owner, new { version = VersionOf(order) }));
        Assert.Equal("confirmed", accepted.GetProperty("status").GetString());
        var preparing = await ReadAsync(await SendAsync(client, HttpMethod.Post, PartnerPath(shop, id, "preparing"), shop.Owner, new { version = VersionOf(accepted) }));
        Assert.Equal("preparing", preparing.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, preparing.GetProperty("estimatedReadyAt").ValueKind);
        var ready = await ReadAsync(await SendAsync(client, HttpMethod.Post, PartnerPath(shop, id, "ready"), shop.Owner, new { version = VersionOf(preparing) }));
        Assert.Equal("readyForPickup", ready.GetProperty("status").GetString());

        // The customer sees the whole story, and can no longer cancel.
        var seen = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{id}", customer));
        Assert.Equal(["placed", "confirmed", "preparing", "readyForPickup"], seen.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("status").GetString()));
        Assert.False(seen.GetProperty("canCancel").GetBoolean());
        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/food/orders", customer));
        Assert.Equal(id, list[0].GetProperty("id").GetString());
    }

    [DatabaseFact]
    public async Task Ordering_is_refused_while_the_food_module_is_off_and_every_endpoint_needs_a_sign_in()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var shop = await OpenShopAsync(client, admin, UniqueName("Closed module"));
        var customer = await SignInAsync(client);
        var off = await SendAsync(client, HttpMethod.Put, "/api/v1/admin/config/modules/food", admin, new { status = "comingSoon" });
        Assert.True(off.IsSuccessStatusCode, await off.Content.ReadAsStringAsync());

        try
        {
            var refused = await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, OrderBody(shop, "off-1"));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("food_not_available", await ErrorCodeAsync(refused));
        }
        finally
        {
            await TurnFoodOnAsync(client, admin);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, "/api/v1/food/quote", null, new { businessId = shop.BusinessId })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", null, OrderBody(shop, "anon"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/food/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/partner/businesses/{shop.BusinessId}/orders")).StatusCode);
    }

    [DatabaseFact]
    public async Task A_missing_key_a_stale_price_and_bad_details_are_refused()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Checks"));
        var customer = await SignInAsync(client);

        var noKey = await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, OrderBody(shop, " "));
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);

        // The dish goes up in price after the customer saw 339.25: the order is refused with the new total.
        var edit = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/partner/businesses/{shop.BusinessId}/menu/items/{shop.Dosa}",
            shop.Owner,
            new { categoryId = shop.Category, name = "Masala dosa", price = 130, isVeg = true, version = shop.DosaVersion });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var stale = await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, OrderBody(shop, "stale-1"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("order_price_changed", await ErrorCodeAsync(stale));
        var fresh = await SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, OrderBody(shop, "stale-1", 360.25m));
        Assert.Equal(HttpStatusCode.Created, fresh.StatusCode);

        var badDetails = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/food/orders",
            customer,
            new { idempotencyKey = "bad-1", businessId = shop.BusinessId, area = "whiteTown", address = "", contactPhone = "nope", lines = DefaultLines(shop), expectedTotal = 360.25m });
        Assert.Equal(HttpStatusCode.BadRequest, badDetails.StatusCode);
    }

    [DatabaseFact]
    public async Task Orders_belong_to_the_customer_and_the_restaurant_alone()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Private"));
        var other = await OpenShopAsync(client, admin, UniqueName("Other restaurant"));
        var customer = await SignInAsync(client);
        var stranger = await SignInAsync(client);
        var order = await PlaceAsync(client, customer, shop);
        var id = Id(order);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{id}", stranger)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{id}/cancel", stranger, new { version = VersionOf(order) })).StatusCode);

        // Another restaurant's owner cannot see or work it, even by putting their own business id on the path.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/orders/{id}", other.Owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, PartnerPath(shop, id, "accept"), other.Owner, new { version = VersionOf(order) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, PartnerPath(other, id, "accept"), other.Owner, new { version = VersionOf(order) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{shop.BusinessId}/orders", other.Owner)).StatusCode);
    }

    [DatabaseFact]
    public async Task The_customer_can_cancel_until_cooking_starts_and_not_after()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Cancels"));
        var customer = await SignInAsync(client);

        var early = await PlaceAsync(client, customer, shop);
        var cancelled = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{Id(early)}/cancel", customer, new { version = VersionOf(early), reason = "Changed my mind" }));
        Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("Changed my mind", cancelled.GetProperty("closedReason").GetString());

        // The restaurant cannot then accept it.
        var accept = await SendAsync(client, HttpMethod.Post, PartnerPath(shop, Id(early), "accept"), shop.Owner, new { version = VersionOf(cancelled) });
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);

        var late = await PlaceAsync(client, customer, shop);
        var confirmed = await ReadAsync(await SendAsync(client, HttpMethod.Post, PartnerPath(shop, Id(late), "accept"), shop.Owner, new { version = VersionOf(late) }));
        var cooking = await ReadAsync(await SendAsync(client, HttpMethod.Post, PartnerPath(shop, Id(late), "preparing"), shop.Owner, new { version = VersionOf(confirmed) }));
        var refused = await SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{Id(late)}/cancel", customer, new { version = VersionOf(cooking) });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("order_invalid_transition", await ErrorCodeAsync(refused));
    }

    [DatabaseFact]
    public async Task A_stale_version_is_refused()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Versions"));
        var customer = await SignInAsync(client);
        var order = await PlaceAsync(client, customer, shop);

        var stale = await SendAsync(client, HttpMethod.Post, PartnerPath(shop, Id(order), "accept"), shop.Owner, new { version = VersionOf(order) + 1000 });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("order_changed", await ErrorCodeAsync(stale));
    }

    [DatabaseFact]
    public async Task The_same_attempt_sent_many_times_at_once_makes_one_order()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Retries"));
        var customer = await SignInAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            SendAsync(client, HttpMethod.Post, "/api/v1/food/orders", customer, OrderBody(shop, "double-tap"))));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created, $"{r.StatusCode}: {r.Content.ReadAsStringAsync().Result}"));
        var ids = new HashSet<string>();
        foreach (var response in responses)
        {
            ids.Add(Id(await response.Content.ReadFromJsonAsync<JsonElement>()));
        }

        Assert.Single(ids);
        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/food/orders", customer));
        Assert.Single(list.EnumerateArray());
    }

    [DatabaseFact]
    public async Task When_the_restaurant_accepts_and_the_customer_cancels_at_once_exactly_one_wins()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Photo finish"));
        var customer = await SignInAsync(client);

        for (var round = 0; round < 5; round++)
        {
            var order = await PlaceAsync(client, customer, shop);
            var version = VersionOf(order);
            var accept = SendAsync(client, HttpMethod.Post, PartnerPath(shop, Id(order), "accept"), shop.Owner, new { version });
            var cancel = SendAsync(client, HttpMethod.Post, $"/api/v1/food/orders/{Id(order)}/cancel", customer, new { version });
            var results = await Task.WhenAll(accept, cancel);

            // Accept first then cancel is fine too (a confirmed order may still be cancelled) but the stale version makes the
            // second one lose: never two winners and never a server error.
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

            var final = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{Id(order)}", customer));
            Assert.Equal(2, final.GetProperty("history").GetArrayLength());
        }
    }

    [DatabaseFact]
    public async Task Two_restaurant_staff_accepting_at_once_make_one_acceptance()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Two taps"));
        var customer = await SignInAsync(client);
        var order = await PlaceAsync(client, customer, shop);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            SendAsync(client, HttpMethod.Post, PartnerPath(shop, Id(order), "accept"), shop.Owner, new { version = VersionOf(order) })));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var final = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/food/orders/{Id(order)}", customer));
        Assert.Equal(2, final.GetProperty("history").GetArrayLength());
    }

    [DatabaseFact]
    public async Task The_database_keeps_orders_honest()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnFoodOnAsync(client, admin);
        var shop = await OpenShopAsync(client, admin, UniqueName("Honest"));
        var customer = await SignInAsync(client);
        var order = await PlaceAsync(client, customer, shop);
        var id = Guid.Parse(Id(order));

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var sql = new NpgsqlConnection(DbOf(scope).Database.GetConnectionString());
        await sql.OpenAsync();

        async Task<PostgresException> RefusedAsync(string text)
        {
            await using var command = sql.CreateCommand();
            command.CommandText = text;
            command.Parameters.AddWithValue("id", id);
            return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        // A line is a record of what was bought: it cannot be edited or removed, and neither can the history.
        Assert.Equal("P0001", (await RefusedAsync("UPDATE food_order_lines SET quantity = 5 WHERE order_id = @id")).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("DELETE FROM food_order_lines WHERE order_id = @id")).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("UPDATE food_order_status_history SET reason = 'edited' WHERE order_id = @id")).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("DELETE FROM food_order_status_history WHERE order_id = @id")).SqlState);

        // The total must add up, and a waiting order must have a deadline.
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE food_orders SET total_amount = total_amount + 1 WHERE id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE food_orders SET expires_at = NULL WHERE id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE food_orders SET status = 'teleported' WHERE id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE food_orders SET delivery_area = 'puducherry' WHERE id = @id")).SqlState);

        // An order whose subtotal is not the sum of its lines (here: no lines at all) cannot be committed.
        await using var transaction = await sql.BeginTransactionAsync();
        await using (var insert = sql.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO food_orders (id, customer_user_id, business_id, status, idempotency_key, expires_at, delivery_area, delivery_address, delivery_note, contact_phone, " +
                "prep_time_minutes, estimated_ready_at, currency, subtotal, delivery_fee, packaging_fee, tax_amount, discount_amount, total_amount, closed_reason, created_at, updated_at) " +
                "SELECT gen_random_uuid(), customer_user_id, business_id, status, 'no-lines', expires_at, delivery_area, delivery_address, delivery_note, contact_phone, " +
                "prep_time_minutes, estimated_ready_at, currency, subtotal, delivery_fee, packaging_fee, tax_amount, discount_amount, total_amount, closed_reason, created_at, updated_at " +
                "FROM food_orders WHERE id = @id";
            insert.Parameters.AddWithValue("id", id);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        var uncommittable = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        Assert.Equal("P0001", uncommittable.SqlState);
    }
}
