using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OnePondy.Application.Identity;
using OnePondy.Domain.Food;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Restaurants and menus (spec Phase 8, step 8.1), against a real, migrated PostgreSQL database: the HTTP flow, what customers
/// can and cannot see, and the rules only the database can enforce. Runs only when ONEPONDY_TEST_DATABASE is set (see README).
/// </summary>
public sealed class FoodApiTests : IAsyncLifetime
{
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

    /// <summary>A partner applies with a restaurant business and, unless told otherwise, an administrator approves it.</summary>
    private async Task<(string Owner, string BusinessId)> RestaurantAsync(HttpClient client, string admin, string name, bool approve = true, string type = "restaurant")
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
                businessType = type,
                description = "Home-style South Indian food.",
                phoneNumber = "9876543210",
                email = "owner@example.com",
                address = "White Town",
                lat = 11.93,
                lng = 79.83,
                gstin = (string?)null,
                fssaiLicenseNumber = type == "restaurant" ? "12345678901234" : null,
            }));
        var businessId = applied.GetProperty("id").GetString()!;
        if (approve)
        {
            var approved = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{businessId}/approve", admin, new { version = applied.GetProperty("version").GetUInt32() });
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        return (owner, businessId);
    }

    private static async Task<JsonElement> AddCategoryAsync(HttpClient client, string owner, string businessId, string name) =>
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/partner/businesses/{businessId}/menu/categories", owner, new { name }));

    private static async Task<JsonElement> AddDishAsync(HttpClient client, string owner, string businessId, string categoryId, string name = "Masala dosa", decimal price = 120m) =>
        await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/partner/businesses/{businessId}/menu/items",
            owner,
            new { categoryId, name, description = "With sambar.", price, isVeg = true }));

    private static async Task<JsonElement> OpenAsync(HttpClient client, string owner, string businessId, params string[] areas) =>
        await ReadAsync(await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/partner/businesses/{businessId}/restaurant",
            owner,
            new { prepTimeMinutes = 30, minOrderAmount = 100, isAcceptingOrders = true, deliveryAreas = areas }));

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

    // --- Tests ---

    [DatabaseFact]
    public async Task A_partner_sets_up_a_menu_opens_the_restaurant_and_customers_see_it_by_area()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var name = UniqueName("Spice Route");
        var (owner, businessId) = await RestaurantAsync(client, admin, name);
        var customer = await SignInAsync(client);

        // Before it is set up: unconfigured, closed, and invisible to customers.
        var before = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{businessId}/restaurant", owner));
        Assert.False(before.GetProperty("configured").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/restaurants/{businessId}")).StatusCode);

        // The menu: a category and a dish.
        var category = await AddCategoryAsync(client, owner, businessId, "Breakfast");
        var dish = await AddDishAsync(client, owner, businessId, category.GetProperty("id").GetString()!);
        Assert.Equal(120m, dish.GetProperty("price").GetDecimal());
        Assert.Equal(5m, dish.GetProperty("taxRatePercent").GetDecimal());
        Assert.True(dish.GetProperty("isAvailable").GetBoolean());

        // Opening needs at least one area, then it is live.
        var open = await OpenAsync(client, owner, businessId, "whiteTown");
        Assert.True(open.GetProperty("isAcceptingOrders").GetBoolean());
        Assert.Equal(["whiteTown"], open.GetProperty("deliveryAreas").EnumerateArray().Select(a => a.GetString()));

        // Customers (even signed out) find it for their area, with its FSSAI number, and read its menu.
        var inWhiteTown = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/restaurants?area=whiteTown", null));
        var listed = inWhiteTown.EnumerateArray().Single(r => r.GetProperty("name").GetString() == name);
        Assert.Equal("12345678901234", listed.GetProperty("fssaiLicenseNumber").GetString());

        var inAuroville = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/restaurants?area=auroville", customer));
        Assert.DoesNotContain(inAuroville.EnumerateArray(), r => r.GetProperty("name").GetString() == name);

        var menu = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/restaurants/{businessId}/menu", null));
        Assert.Equal("Masala dosa", menu.GetProperty("categories")[0].GetProperty("items")[0].GetProperty("name").GetString());
    }

    [DatabaseFact]
    public async Task Partner_endpoints_need_a_sign_in_and_belong_to_the_owner_alone()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId) = await RestaurantAsync(client, admin, UniqueName("Mine"));
        var stranger = await SignInAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/partner/businesses/{businessId}/menu")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Put, $"/api/v1/partner/businesses/{businessId}/restaurant", null, new { prepTimeMinutes = 30, minOrderAmount = 0, isAcceptingOrders = false, deliveryAreas = Array.Empty<string>() })).StatusCode);

        var read = await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{businessId}/menu", stranger);
        var write = await SendAsync(client, HttpMethod.Post, $"/api/v1/partner/businesses/{businessId}/menu/categories", stranger, new { name = "Hijack" });
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.Equal("restaurant_not_found", await ErrorCodeAsync(write));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{businessId}/menu", owner)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_business_that_is_not_a_restaurant_is_refused_and_one_not_yet_approved_cannot_open()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (hotelOwner, hotelId) = await RestaurantAsync(client, admin, UniqueName("Sea View"), type: "hotel");
        var (owner, pendingId) = await RestaurantAsync(client, admin, UniqueName("Pending"), approve: false);

        var notRestaurant = await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{hotelId}/menu", hotelOwner);
        Assert.Equal(HttpStatusCode.BadRequest, notRestaurant.StatusCode);
        Assert.Equal("business_not_a_restaurant", await ErrorCodeAsync(notRestaurant));

        var category = await AddCategoryAsync(client, owner, pendingId, "Mains");
        await AddDishAsync(client, owner, pendingId, category.GetProperty("id").GetString()!);
        var open = await SendAsync(client, HttpMethod.Put, $"/api/v1/partner/businesses/{pendingId}/restaurant", owner, new { prepTimeMinutes = 30, minOrderAmount = 0, isAcceptingOrders = true, deliveryAreas = new[] { "whiteTown" } });
        Assert.Equal("restaurant_business_not_active", await ErrorCodeAsync(open));
    }

    [DatabaseFact]
    public async Task Opening_needs_an_area_and_an_available_dish_and_unknown_areas_are_refused()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId) = await RestaurantAsync(client, admin, UniqueName("Empty"));
        var path = $"/api/v1/partner/businesses/{businessId}/restaurant";

        var noDish = await SendAsync(client, HttpMethod.Put, path, owner, new { prepTimeMinutes = 30, minOrderAmount = 0, isAcceptingOrders = true, deliveryAreas = new[] { "whiteTown" } });
        Assert.Equal("restaurant_menu_empty", await ErrorCodeAsync(noDish));

        var category = await AddCategoryAsync(client, owner, businessId, "Mains");
        await AddDishAsync(client, owner, businessId, category.GetProperty("id").GetString()!);
        var noArea = await SendAsync(client, HttpMethod.Put, path, owner, new { prepTimeMinutes = 30, minOrderAmount = 0, isAcceptingOrders = true, deliveryAreas = Array.Empty<string>() });
        var badArea = await SendAsync(client, HttpMethod.Put, path, owner, new { prepTimeMinutes = 30, minOrderAmount = 0, isAcceptingOrders = false, deliveryAreas = new[] { "narnia" } });
        Assert.Equal(HttpStatusCode.BadRequest, noArea.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badArea.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/restaurants?area=narnia")).StatusCode);
    }

    [DatabaseFact]
    public async Task A_stale_version_is_refused_and_the_current_one_is_accepted()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId) = await RestaurantAsync(client, admin, UniqueName("Versions"));
        var path = $"/api/v1/partner/businesses/{businessId}/restaurant";
        var first = await ReadAsync(await SendAsync(client, HttpMethod.Put, path, owner, new { prepTimeMinutes = 30, minOrderAmount = 0, isAcceptingOrders = false, deliveryAreas = new[] { "whiteTown" } }));
        var version = first.GetProperty("version").GetUInt32();

        var second = await SendAsync(client, HttpMethod.Put, path, owner, new { prepTimeMinutes = 45, minOrderAmount = 0, isAcceptingOrders = false, deliveryAreas = new[] { "whiteTown" }, version });
        var stale = await SendAsync(client, HttpMethod.Put, path, owner, new { prepTimeMinutes = 50, minOrderAmount = 0, isAcceptingOrders = false, deliveryAreas = new[] { "whiteTown" }, version });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("restaurant_changed", await ErrorCodeAsync(stale));
    }

    [DatabaseFact]
    public async Task Dishes_can_be_marked_sold_out_and_archived_and_customers_see_only_what_they_should()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId) = await RestaurantAsync(client, admin, UniqueName("Menu"));
        var category = (await AddCategoryAsync(client, owner, businessId, "Mains")).GetProperty("id").GetString()!;
        var stays = await AddDishAsync(client, owner, businessId, category, "Biryani", 220m);
        var soldOut = await AddDishAsync(client, owner, businessId, category, "Fish curry", 260m);
        var gone = await AddDishAsync(client, owner, businessId, category, "Retired dish", 90m);
        await OpenAsync(client, owner, businessId, "whiteTown");
        var basePath = $"/api/v1/partner/businesses/{businessId}/menu/items";

        var flipped = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{basePath}/{soldOut.GetProperty("id").GetString()}/availability", owner, new { isAvailable = false, version = soldOut.GetProperty("version").GetUInt32() }));
        var archived = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{basePath}/{gone.GetProperty("id").GetString()}/archive", owner, new { version = gone.GetProperty("version").GetUInt32() }));
        Assert.False(flipped.GetProperty("isAvailable").GetBoolean());
        Assert.True(archived.GetProperty("isArchived").GetBoolean());

        var menu = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/restaurants/{businessId}/menu", null));
        var items = menu.GetProperty("categories")[0].GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Biryani", "Fish curry"], items.Select(i => i.GetProperty("name").GetString()).Order());
        Assert.False(items.Single(i => i.GetProperty("name").GetString() == "Fish curry").GetProperty("isAvailable").GetBoolean());
        Assert.True(items.Single(i => i.GetProperty("name").GetString() == "Biryani").GetProperty("isAvailable").GetBoolean());
        Assert.Equal(stays.GetProperty("id").GetString(), items.Single(i => i.GetProperty("name").GetString() == "Biryani").GetProperty("id").GetString());

        // An archived dish cannot be brought back or edited.
        var edit = await SendAsync(client, HttpMethod.Put, $"{basePath}/{gone.GetProperty("id").GetString()}", owner, new { categoryId = category, name = "Back again", price = 90, isVeg = true, version = archived.GetProperty("version").GetUInt32() });
        Assert.Equal("menu_item_archived", await ErrorCodeAsync(edit));
    }

    [DatabaseFact]
    public async Task A_closed_or_suspended_restaurant_vanishes_for_customers()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var name = UniqueName("Vanishing");
        var (owner, businessId) = await RestaurantAsync(client, admin, name);
        var category = await AddCategoryAsync(client, owner, businessId, "Mains");
        await AddDishAsync(client, owner, businessId, category.GetProperty("id").GetString()!);
        var opened = await OpenAsync(client, owner, businessId, FoodAreas.WholeCity);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/restaurants/{businessId}")).StatusCode);

        // The partner closes for the day.
        var path = $"/api/v1/partner/businesses/{businessId}/restaurant";
        var closed = await ReadAsync(await SendAsync(client, HttpMethod.Put, path, owner, new { prepTimeMinutes = 30, minOrderAmount = 100, isAcceptingOrders = false, deliveryAreas = new[] { FoodAreas.WholeCity }, version = opened.GetProperty("version").GetUInt32() }));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/restaurants/{businessId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/restaurants/{businessId}/menu")).StatusCode);

        // Reopen, then an administrator suspends the business.
        await ReadAsync(await SendAsync(client, HttpMethod.Put, path, owner, new { prepTimeMinutes = 30, minOrderAmount = 100, isAcceptingOrders = true, deliveryAreas = new[] { FoodAreas.WholeCity }, version = closed.GetProperty("version").GetUInt32() }));
        var detail = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/admin/businesses/{businessId}", admin));
        var suspend = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{businessId}/suspend", admin, new { version = detail.GetProperty("version").GetUInt32(), reason = "Hygiene complaint" });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/restaurants/{businessId}")).StatusCode);
        var list = await ReadAsync(await client.GetAsync("/api/v1/restaurants"));
        Assert.DoesNotContain(list.EnumerateArray(), r => r.GetProperty("name").GetString() == name);
    }

    [DatabaseFact]
    public async Task Two_categories_with_the_same_name_added_at_once_make_only_one()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId) = await RestaurantAsync(client, admin, UniqueName("Race"));

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            SendAsync(client, HttpMethod.Post, $"/api/v1/partner/businesses/{businessId}/menu/categories", owner, new { name = "Starters" })));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.True(r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict));
        var menu = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{businessId}/menu", owner));
        Assert.Single(menu.GetProperty("categories").EnumerateArray());
    }

    [DatabaseFact]
    public async Task The_database_refuses_a_dish_in_another_restaurants_category_and_bad_prices()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (mineOwner, mine) = await RestaurantAsync(client, admin, UniqueName("Mine"));
        var (theirOwner, theirs) = await RestaurantAsync(client, admin, UniqueName("Theirs"));
        var theirCategory = (await AddCategoryAsync(client, theirOwner, theirs, "Mains")).GetProperty("id").GetString()!;
        var myCategory = (await AddCategoryAsync(client, mineOwner, mine, "Mains")).GetProperty("id").GetString()!;

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var now = DateTimeOffset.UtcNow;

        // Straight to the database, past the service's own checks: a dish whose category belongs to another restaurant.
        var wrongRestaurant = MenuItem.Create(Guid.Parse(mine), Guid.Parse(theirCategory), new MenuItemContent("Smuggled", null, 50m, true, 5m), 0, now);
        db.MenuItems.Add(wrongRestaurant);
        var fk = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (fk.InnerException as PostgresException)?.SqlState);
        db.ChangeTracker.Clear();

        // A zero price is refused by the table's own rule.
        await using var sql = new NpgsqlConnection(db.Database.GetConnectionString());
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = "INSERT INTO menu_items (id, business_id, category_id, name, price, is_veg, tax_rate_percent, is_available, is_archived, position, created_at, updated_at) " +
            "VALUES (gen_random_uuid(), @b, @c, 'Free lunch', 0, true, 5, true, false, 0, now(), now())";
        command.Parameters.AddWithValue("b", Guid.Parse(mine));
        command.Parameters.AddWithValue("c", Guid.Parse(myCategory));
        var check = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, check.SqlState);
    }
}
