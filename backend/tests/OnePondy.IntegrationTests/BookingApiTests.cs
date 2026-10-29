using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using OnePondy.Application.Bookings;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Listings, availability and the booking engine (spec §7, §10, Phase 6), against a real, migrated
/// PostgreSQL database. Runs only when ONEPONDY_TEST_DATABASE is set (see README).
/// </summary>
public sealed class BookingApiTests : IAsyncLifetime
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

        // Codes can be re-requested at once here (the deletion test asks for two); the limits
        // themselves are covered by unit tests.
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

    private async Task<string> SignInCustomerAsync(HttpClient client) => (await SignInWithPhoneAsync(client)).Token;

    private async Task<(string Token, string Phone)> SignInWithPhoneAsync(HttpClient client)
    {
        var phone = ApiFactory.NewIndianMobile();
        var requested = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = phone });
        var challengeId = (await ReadAsync(requested)).GetProperty("challengeId").GetString();
        var verified = await client.PostAsJsonAsync(
            "/api/v1/auth/otp/verify",
            new { challengeId, code = Factory.Codes.For("+91" + phone) });
        return ((await ReadAsync(verified)).GetProperty("accessToken").GetString()!, phone);
    }

    /// <summary>Asks to delete the account with the one-time code the API "sent", and returns the confirm response.</summary>
    private async Task<HttpResponseMessage> TryDeleteAccountAsync(HttpClient client, string token, string phone)
    {
        var requested = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/account/deletion/code", token));
        return await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/account/deletion/confirm",
            token,
            new { challengeId = requested.GetProperty("challengeId").GetString(), code = Factory.Codes.For("+91" + phone) });
    }

    /// <summary>Applies as a partner, approves the business, and creates, submits and publishes a bookable listing with one slot.</summary>
    private async Task<(string Owner, string BusinessId, string ListingId, string SlotId)> PublishedListingAsync(
        HttpClient client,
        string admin,
        int capacity = 2)
    {
        var owner = await SignInCustomerAsync(client);
        var applied = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/partner/businesses",
            owner,
            new
            {
                name = "Backwater Adventures",
                businessType = "activity",
                description = "Kayak and canoe tours.",
                phoneNumber = "9876543210",
                email = "owner@example.com",
                address = "Puducherry",
                lat = 11.9,
                lng = 79.83,
                gstin = (string?)null,
                fssaiLicenseNumber = (string?)null,
            }));
        var businessId = applied.GetProperty("id").GetString()!;
        await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{businessId}/approve", admin, new { version = applied.GetProperty("version").GetUInt32() });

        var listing = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/partner/businesses/{businessId}/listings",
            owner,
            new
            {
                listingType = "activity",
                title = "Sunset Kayak Tour",
                description = "A two-hour guided kayak tour at sunset.",
                isBookable = true,
                basePrice = 500,
                freeCancellationHours = 24,
                refundPercentage = 50,
            }));
        var listingId = listing.GetProperty("id").GetString()!;
        var submitted = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, $"/api/v1/partner/businesses/{businessId}/listings/{listingId}/submit", owner, new { version = listing.GetProperty("version").GetUInt32() }));
        await SendAsync(
            client, HttpMethod.Post, $"/api/v1/admin/listings/{listingId}/publish", admin, new { version = submitted.GetProperty("version").GetUInt32() });

        var start = DateTimeOffset.UtcNow.AddDays(1);
        var slot = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/partner/businesses/{businessId}/listings/{listingId}/slots",
            owner,
            new { startAt = start, endAt = start.AddHours(2), capacity }));

        return (owner, businessId, listingId, slot.GetProperty("id").GetString()!);
    }

    [DatabaseFact]
    public async Task A_booking_goes_from_pending_to_confirmed_to_completed()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId, listingId, slotId) = await PublishedListingAsync(client, admin);
        var customer = await SignInCustomerAsync(client);

        var created = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 2, idempotencyKey = Guid.NewGuid().ToString() }));
        Assert.Equal("pending", created.GetProperty("status").GetString());
        var bookingId = created.GetProperty("id").GetString();
        var version = created.GetProperty("version").GetUInt32();

        var accepted = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, $"/api/v1/partner/businesses/{businessId}/bookings/{bookingId}/accept", owner, new { version }));
        Assert.Equal("confirmed", accepted.GetProperty("status").GetString());

        var completed = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, $"/api/v1/partner/businesses/{businessId}/bookings/{bookingId}/complete", owner, new { version = accepted.GetProperty("version").GetUInt32() }));
        Assert.Equal("completed", completed.GetProperty("status").GetString());

        var mine = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/bookings", customer));
        Assert.Equal(bookingId, Assert.Single(mine.EnumerateArray()).GetProperty("id").GetString());
    }

    [DatabaseFact]
    public async Task Listings_availability_and_the_partner_inbox_can_be_browsed()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId, listingId, slotId) = await PublishedListingAsync(client, admin);
        var customer = await SignInCustomerAsync(client);

        var all = await ReadAsync(await client.GetAsync("/api/v1/listings?pageSize=100"));
        Assert.Contains(all.GetProperty("items").EnumerateArray(), l => l.GetProperty("id").GetString() == listingId);
        var activities = await ReadAsync(await client.GetAsync("/api/v1/listings?type=activity&pageSize=100"));
        Assert.Contains(activities.GetProperty("items").EnumerateArray(), l => l.GetProperty("id").GetString() == listingId);
        var stays = await ReadAsync(await client.GetAsync("/api/v1/listings?type=stay&pageSize=100"));
        Assert.DoesNotContain(stays.GetProperty("items").EnumerateArray(), l => l.GetProperty("id").GetString() == listingId);

        var detail = await ReadAsync(await client.GetAsync($"/api/v1/listings/{listingId}"));
        Assert.Equal("Backwater Adventures", detail.GetProperty("businessName").GetString());
        var availability = await ReadAsync(await client.GetAsync($"/api/v1/listings/{listingId}/availability"));
        Assert.Equal(slotId, Assert.Single(availability.EnumerateArray()).GetProperty("id").GetString());

        var adminList = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/listings?status=published&pageSize=100", admin));
        Assert.Contains(adminList.GetProperty("items").EnumerateArray(), l => l.GetProperty("id").GetString() == listingId);

        await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() }));

        var inbox = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{businessId}/bookings", owner));
        Assert.Equal("Sunset Kayak Tour", Assert.Single(inbox.EnumerateArray()).GetProperty("listingTitle").GetString());
        var pending = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{businessId}/bookings?status=pending", owner));
        Assert.Single(pending.EnumerateArray());
        var confirmed = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{businessId}/bookings?status=confirmed", owner));
        Assert.Empty(confirmed.EnumerateArray());
    }

    [DatabaseFact]
    public async Task Saving_a_listing_needs_it_to_be_live_and_is_idempotent()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId, listingId, _) = await PublishedListingAsync(client, admin);
        var draft = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/partner/businesses/{businessId}/listings",
            owner,
            new { listingType = "activity", title = "Not live yet", isBookable = true, basePrice = 100, freeCancellationHours = 1, refundPercentage = 0 }));
        var customer = await SignInCustomerAsync(client);

        var draftSave = await SendAsync(client, HttpMethod.Post, "/api/v1/saved-listings", customer, new { listingId = draft.GetProperty("id").GetString() });
        Assert.Equal(HttpStatusCode.BadRequest, draftSave.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, "/api/v1/saved-listings", customer, new { listingId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, "/api/v1/saved-listings", customer, new { listingId })).StatusCode);

        var saved = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/saved-listings", customer));
        Assert.Equal("Sunset Kayak Tour", Assert.Single(saved.EnumerateArray()).GetProperty("listing").GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/api/v1/saved-listings/{listingId}", customer)).StatusCode);
        Assert.Empty((await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/saved-listings", customer))).EnumerateArray());
    }

    [DatabaseFact]
    public async Task Deleting_an_account_waits_for_bookings_in_progress()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, _, listingId, slotId) = await PublishedListingAsync(client, admin);
        var (customer, phone) = await SignInWithPhoneAsync(client);
        var userId = Guid.Parse((await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/profile", customer))).GetProperty("id").GetString()!);
        await SendAsync(client, HttpMethod.Post, "/api/v1/saved-listings", customer, new { listingId });
        var booking = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() }));

        // The signed-in user hears about it before any code is sent.
        var blocked = await SendAsync(client, HttpMethod.Post, "/api/v1/account/deletion/code", customer);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("deletion_blocked", problem.GetProperty("code").GetString());
        Assert.Contains("bookings_in_progress", problem.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));

        await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/bookings/{booking.GetProperty("id").GetString()}/cancel",
            customer,
            new { version = booking.GetProperty("version").GetUInt32() }));

        Assert.Equal(HttpStatusCode.NoContent, (await TryDeleteAccountAsync(client, customer, phone)).StatusCode);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();
        Assert.False(await db.SavedListings.AnyAsync(s => s.UserId == userId));
    }

    [DatabaseFact]
    public async Task Two_customers_racing_for_the_last_place_never_both_get_it()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();

        // Several rounds, each on a fresh one-place slot, because a single race may not interleave.
        for (var round = 0; round < 6; round++)
        {
            var (_, _, listingId, slotId) = await PublishedListingAsync(client, admin, capacity: 1);
            var first = await SignInCustomerAsync(client);
            var second = await SignInCustomerAsync(client);

            var responses = await Task.WhenAll(
                SendAsync(client, HttpMethod.Post, "/api/v1/bookings", first, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() }),
                SendAsync(client, HttpMethod.Post, "/api/v1/bookings", second, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() }));

            var codes = responses.Select(r => r.StatusCode).OrderBy(c => c).ToList();
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], codes);

            var slots = await ReadAsync(await client.GetAsync($"/api/v1/listings/{listingId}/availability"));
            Assert.Equal(1, Assert.Single(slots.EnumerateArray()).GetProperty("reservedCount").GetInt32());
        }
    }

    [DatabaseFact]
    public async Task A_slot_with_booking_history_is_refused_cleanly_rather_than_failing()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId, listingId, slotId) = await PublishedListingAsync(client, admin);
        var customer = await SignInCustomerAsync(client);
        var booking = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() }));
        await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/bookings/{booking.GetProperty("id").GetString()}/cancel",
            customer,
            new { version = booking.GetProperty("version").GetUInt32() }));

        var removed = await SendAsync(client, HttpMethod.Delete, $"/api/v1/partner/businesses/{businessId}/listings/{listingId}/slots/{slotId}", owner);

        Assert.Equal(HttpStatusCode.Conflict, removed.StatusCode);
    }

    [DatabaseFact]
    public async Task A_bookings_status_history_cannot_be_changed_or_removed()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, _, listingId, slotId) = await PublishedListingAsync(client, admin);
        var customer = await SignInCustomerAsync(client);
        var booking = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() }));
        var bookingId = Guid.Parse(booking.GetProperty("id").GetString()!);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();

        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE booking_status_history SET reason = 'edited'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM booking_status_history"));
        var truncate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE booking_status_history"));
        Assert.Contains("append-only", update.MessageText);
        Assert.Contains("append-only", delete.MessageText);
        Assert.Contains("append-only", truncate.MessageText);

        // A booking with history cannot be deleted either: the history is never cascade-deleted.
        var deleteBooking = await Assert.ThrowsAsync<PostgresException>(
            () => db.Database.ExecuteSqlAsync($"DELETE FROM bookings WHERE id = {bookingId}"));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, deleteBooking.SqlState);
    }

    [DatabaseFact]
    public async Task A_second_booking_cannot_take_more_than_the_slot_has_left()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, _, listingId, slotId) = await PublishedListingAsync(client, admin, capacity: 2);
        var first = await SignInCustomerAsync(client);
        var second = await SignInCustomerAsync(client);

        var firstBooking = await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", first, new { listingId, slotIds = new[] { slotId }, guestCount = 2, idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, firstBooking.StatusCode);

        var secondBooking = await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", second, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Conflict, secondBooking.StatusCode);
    }

    [DatabaseFact]
    public async Task Retrying_a_booking_with_the_same_idempotency_key_returns_the_original_booking()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, _, listingId, slotId) = await PublishedListingAsync(client, admin);
        var customer = await SignInCustomerAsync(client);
        var key = Guid.NewGuid().ToString();

        var first = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = key }));
        var second = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = key }));

        Assert.Equal(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
    }

    [DatabaseFact]
    public async Task Rejecting_a_booking_frees_the_slot_for_someone_else()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (owner, businessId, listingId, slotId) = await PublishedListingAsync(client, admin, capacity: 2);
        var first = await SignInCustomerAsync(client);
        var second = await SignInCustomerAsync(client);

        var booking = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", first, new { listingId, slotIds = new[] { slotId }, guestCount = 2, idempotencyKey = Guid.NewGuid().ToString() }));

        var rejected = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/partner/businesses/{businessId}/bookings/{booking.GetProperty("id").GetString()}/reject",
            owner,
            new { version = booking.GetProperty("version").GetUInt32(), reason = "Weather conditions are unsafe." }));
        Assert.Equal("rejected", rejected.GetProperty("status").GetString());

        var afterReject = await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", second, new { listingId, slotIds = new[] { slotId }, guestCount = 2, idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, afterReject.StatusCode);
    }

    [DatabaseFact]
    public async Task Only_the_owning_partner_can_see_or_act_on_a_booking()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, _, listingId, slotId) = await PublishedListingAsync(client, admin);
        var (stranger, strangerBusinessId, _, _) = await PublishedListingAsync(client, admin);
        var customer = await SignInCustomerAsync(client);

        var booking = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() }));

        var denied = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/partner/businesses/{strangerBusinessId}/bookings/{booking.GetProperty("id").GetString()}/accept",
            stranger,
            new { version = booking.GetProperty("version").GetUInt32() });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [DatabaseFact]
    public async Task An_expired_hold_releases_its_slot()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, _, listingId, slotId) = await PublishedListingAsync(client, admin, capacity: 1);
        var customer = await SignInCustomerAsync(client);

        var booking = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() }));
        var bookingId = Guid.Parse(booking.GetProperty("id").GetString()!);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();
            var later = new FakeTimeProvider(DateTimeOffset.UtcNow + OnePondy.Domain.Bookings.Booking.HoldDuration + TimeSpan.FromMinutes(5));
            var expiredCount = await new BookingHoldExpiry(db, new BookingService(db, later), later).ExpireDueHoldsAsync(default);
            Assert.True(expiredCount >= 1);
        }

        var another = await SignInCustomerAsync(client);
        var afterExpiry = await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", another, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, afterExpiry.StatusCode);

        var expired = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));
        Assert.Equal("expired", expired.GetProperty("status").GetString());
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        var request = Authorized(method, path, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
