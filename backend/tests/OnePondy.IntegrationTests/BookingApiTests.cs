using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
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

        _factory = new ApiFactory(connectionString);
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

    private async Task<string> SignInCustomerAsync(HttpClient client)
    {
        var phone = ApiFactory.NewIndianMobile();
        var requested = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = phone });
        var challengeId = (await ReadAsync(requested)).GetProperty("challengeId").GetString();
        var verified = await client.PostAsJsonAsync(
            "/api/v1/auth/otp/verify",
            new { challengeId, code = Factory.Codes.For("+91" + phone) });
        return (await ReadAsync(verified)).GetProperty("accessToken").GetString()!;
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
