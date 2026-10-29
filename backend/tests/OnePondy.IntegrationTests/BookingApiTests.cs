using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using OnePondy.Application.Bookings;
using OnePondy.Application.Identity;
using OnePondy.Application.Notifications;
using OnePondy.Application.Payments;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Listings, availability and the booking engine (spec §7, §10, Phase 6), against a real, migrated
/// PostgreSQL database. Runs only when ONEPONDY_TEST_DATABASE is set (see README).
/// </summary>
public sealed class BookingApiTests : IAsyncLifetime
{
    private const string WebhookSecret = "integration-webhook-secret";

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
            var expiredCount = (await ExpiryFor(scope, new FakePaymentGateway(), later).ExpireDueHoldsAsync(default)).Expired;
            Assert.True(expiredCount >= 1);
        }

        var another = await SignInCustomerAsync(client);
        var afterExpiry = await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", another, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, afterExpiry.StatusCode);

        var expired = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));
        Assert.Equal("expired", expired.GetProperty("status").GetString());
    }

    // --- Phase 7 step 7.1: payments schema and the awaiting_payment flow, against real PostgreSQL ---

    private async Task<Guid> NewBookingIdAsync() => (await NewBookingAsync()).BookingId;

    private async Task<(string Customer, Guid BookingId)> NewBookingAsync()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, _, listingId, slotId) = await PublishedListingAsync(client, admin);
        var customer = await SignInCustomerAsync(client);
        var booking = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, "/api/v1/bookings", customer, new { listingId, slotIds = new[] { slotId }, guestCount = 1, idempotencyKey = Guid.NewGuid().ToString() }));
        return (customer, Guid.Parse(booking.GetProperty("id").GetString()!));
    }

    /// <summary>A booking the partner has accepted for payment, made the way the accept endpoint will once payments are on.</summary>
    private async Task<(string Customer, Guid BookingId)> AwaitingPaymentBookingAsync()
    {
        var (customer, bookingId) = await NewBookingAsync();
        var partner = await PartnerOfAsync(bookingId);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId);
        booking.AcceptForPayment(partner, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
        db.MarkAdded(booking.History[^1]);
        await db.SaveChangesAsync();
        return (customer, bookingId);
    }

    /// <summary>The API with a fake payment gateway and a test Razorpay key, so the endpoints can be exercised without the network.</summary>
    private WebApplicationFactory<Program> WithFakeGateway(FakePaymentGateway gateway, bool refundsEnabled = false) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Payments:RefundsEnabled", refundsEnabled ? "true" : "false");
            builder.UseSetting("Payments:Razorpay:KeyId", "rzp_test_integration");
            builder.UseSetting("Payments:Razorpay:KeySecret", "integration-secret");
            builder.UseSetting("Payments:Razorpay:WebhookSecret", WebhookSecret);
            builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(gateway));
        });

    /// <summary>The Worker hold-expiry step on a real database, with a fake gateway and an optional controllable clock.</summary>
    private BookingHoldExpiry ExpiryFor(AsyncServiceScope scope, FakePaymentGateway gateway, TimeProvider? clock = null)
    {
        var db = DbOf(scope);
        var time = clock ?? TimeProvider.System;
        var options = Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = "rzp_test_integration" } });
        var wired = Wire(db, gateway, time, options);
        return new BookingHoldExpiry(db, wired.Bookings, wired.Payments, time, options);
    }

    /// <summary>Pushes a booking hold into the past, the way waiting out the payment window would.</summary>
    private async Task MakeOverdueAsync(Guid bookingId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await DbOf(scope).Database.ExecuteSqlAsync($"UPDATE bookings SET expires_at = now() - interval '1 minute' WHERE id = {bookingId}");
    }

    private async Task<int> ReservedForAsync(Guid bookingId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        return await db.BookingSlots.AsNoTracking()
            .Where(bs => bs.BookingId == bookingId)
            .Join(db.AvailabilitySlots.AsNoTracking(), bs => bs.SlotId, s => s.Id, (_, s) => s.ReservedCount)
            .SingleAsync();
    }

    private PaymentService PaymentServiceFor(AsyncServiceScope scope, FakePaymentGateway gateway) =>
        Wire(
            DbOf(scope),
            gateway,
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = "rzp_test_integration" } })).Payments;

    /// <summary>The booking, payment, refund and ledger services built by hand on one database, with a fake gateway.</summary>
    private static (BookingService Bookings, PaymentService Payments, RefundService Refunds, LedgerService Ledger, ReconciliationService Reconciliation) Wire(
        OnePondyDbContext db,
        FakePaymentGateway gateway,
        TimeProvider time,
        Microsoft.Extensions.Options.IOptions<PaymentsOptions> options)
    {
        var ledger = new LedgerService(db, time, options);
        var refunds = new RefundService(db, gateway, time, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<RefundService>.Instance);
        var notifications = new OnePondy.Application.Notifications.NotificationOutbox(db, time);
        var payments = new PaymentService(db, gateway, time, options, ledger, refunds, notifications, Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentService>.Instance);
        var reconciliation = new ReconciliationService(db, gateway, payments, time, Microsoft.Extensions.Logging.Abstractions.NullLogger<ReconciliationService>.Instance);
        return (new BookingService(db, time, options, refunds, notifications), payments, refunds, ledger, reconciliation);
    }

    private static OnePondyDbContext DbOf(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();

    /// <summary>The user who owns the business a booking is for: a real account, as the history table requires.</summary>
    private async Task<Guid> PartnerOfAsync(Guid bookingId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var businessId = await db.Bookings.AsNoTracking().Where(b => b.Id == bookingId).Select(b => b.BusinessId).SingleAsync();
        return await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.OwnerUserId).SingleAsync();
    }

    [DatabaseFact]
    public async Task The_payments_module_flag_exists_and_starts_off()
    {
        await using var scope = Factory.Services.CreateAsyncScope();

        var flag = await DbOf(scope).ModuleFlags.AsNoTracking().SingleAsync(f => f.Key == "payments");

        Assert.Equal(OnePondy.Domain.Configuration.ModuleStatus.ComingSoon, flag.Status);
    }

    [DatabaseFact]
    public async Task A_booking_can_wait_for_payment_then_be_confirmed_by_the_system()
    {
        var bookingId = await NewBookingIdAsync();
        var partner = await PartnerOfAsync(bookingId);
        var now = DateTimeOffset.UtcNow;

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId);
            booking.AcceptForPayment(partner, now, TimeSpan.FromMinutes(30));
            db.MarkAdded(booking.History[^1]);
            await db.SaveChangesAsync();
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId);
            Assert.Equal(OnePondy.Domain.Bookings.BookingStatus.AwaitingPayment, booking.Status);
            Assert.InRange(booking.ExpiresAt!.Value, now.AddMinutes(29), now.AddMinutes(31));

            booking.ConfirmPayment(now.AddMinutes(5));
            db.MarkAdded(booking.History[^1]);
            await db.SaveChangesAsync();
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var booking = await DbOf(scope).Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
            Assert.Equal(OnePondy.Domain.Bookings.BookingStatus.Confirmed, booking.Status);
            Assert.Null(booking.ExpiresAt);
        }
    }

    [DatabaseFact]
    public async Task An_unpaid_booking_is_expired_by_the_worker_and_its_slot_freed()
    {
        var bookingId = await NewBookingIdAsync();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId);
            booking.AcceptForPayment(await PartnerOfAsync(bookingId), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
            db.MarkAdded(booking.History[^1]);
            await db.SaveChangesAsync();
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            var service = Wire(db, new FakePaymentGateway(), TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new PaymentsOptions())).Bookings;
            Assert.True(await service.TryExpireAsync(bookingId, default));
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
            Assert.Equal(OnePondy.Domain.Bookings.BookingStatus.Expired, booking.Status);
            var reserved = await db.BookingSlots.AsNoTracking()
                .Where(bs => bs.BookingId == bookingId)
                .Join(db.AvailabilitySlots.AsNoTracking(), bs => bs.SlotId, s => s.Id, (_, s) => s.ReservedCount)
                .SingleAsync();
            Assert.Equal(0, reserved);
        }
    }

    [DatabaseFact]
    public async Task A_payment_order_id_can_only_be_used_once()
    {
        var bookingId = await NewBookingIdAsync();
        var orderId = "order_" + Guid.NewGuid().ToString("N");
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        db.PaymentAttempts.Add(OnePondy.Domain.Payments.PaymentAttempt.Create(bookingId, orderId, 500m, "INR", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        db.PaymentAttempts.Add(OnePondy.Domain.Payments.PaymentAttempt.Create(bookingId, orderId, 500m, "INR", DateTimeOffset.UtcNow));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)failure.InnerException!).SqlState);
    }

    [DatabaseFact]
    public async Task A_captured_payment_must_name_the_gateway_payment()
    {
        var bookingId = await NewBookingIdAsync();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var attempt = OnePondy.Domain.Payments.PaymentAttempt.Create(bookingId, "order_" + Guid.NewGuid().ToString("N"), 500m, "INR", DateTimeOffset.UtcNow);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => db.Database.ExecuteSqlAsync($"UPDATE payment_attempts SET status = 'captured' WHERE id = {attempt.Id}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }

    [DatabaseFact]
    public async Task A_webhook_delivered_twice_is_recorded_once()
    {
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        db.GatewayEvents.Add(OnePondy.Domain.Payments.GatewayEvent.Receive(eventId, "payment.captured", """{"event":"payment.captured"}""", true, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        db.GatewayEvents.Add(OnePondy.Domain.Payments.GatewayEvent.Receive(eventId, "payment.captured", """{"event":"payment.captured"}""", true, DateTimeOffset.UtcNow));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)failure.InnerException!).SqlState);
        Assert.Equal(1, await db.GatewayEvents.AsNoTracking().CountAsync(e => e.ProviderEventId == eventId));
    }

    [DatabaseFact]
    public async Task A_balanced_ledger_transaction_commits_and_an_unbalanced_one_is_refused()
    {
        var business = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var balanced = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);

        db.LedgerEntries.AddRange(
            OnePondy.Domain.Payments.LedgerEntry.Create(balanced, OnePondy.Domain.Payments.LedgerAccounts.GatewayClearing, 1000m, null, "Captured", now),
            OnePondy.Domain.Payments.LedgerEntry.Create(balanced, OnePondy.Domain.Payments.LedgerAccounts.PlatformCommission, -100m, null, "Commission", now),
            OnePondy.Domain.Payments.LedgerEntry.Create(balanced, OnePondy.Domain.Payments.LedgerAccounts.PartnerPayable(business), -900m, null, "Partner share", now));
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.LedgerEntries.AsNoTracking().CountAsync(e => e.TransactionId == balanced));

        var unbalanced = Guid.NewGuid();
        db.LedgerEntries.AddRange(
            OnePondy.Domain.Payments.LedgerEntry.Create(unbalanced, OnePondy.Domain.Payments.LedgerAccounts.GatewayClearing, 1000m, null, "Captured", now),
            OnePondy.Domain.Payments.LedgerEntry.Create(unbalanced, OnePondy.Domain.Payments.LedgerAccounts.PlatformCommission, -100m, null, "Commission only", now));
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.SaveChangesAsync());
        Assert.Contains("does not balance", failure.MessageText);
    }

    [DatabaseFact]
    public async Task Ledger_entries_cannot_be_changed_removed_or_given_an_unknown_account()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);

        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE ledger_entries SET description = 'edited'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM ledger_entries"));
        var truncate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE ledger_entries"));
        Assert.Contains("append-only", update.MessageText);
        Assert.Contains("append-only", delete.MessageText);
        Assert.Contains("append-only", truncate.MessageText);

        var transaction = Guid.NewGuid();
        db.LedgerEntries.AddRange(
            OnePondy.Domain.Payments.LedgerEntry.Create(transaction, "made_up_account", 10m, null, "x", DateTimeOffset.UtcNow),
            OnePondy.Domain.Payments.LedgerEntry.Create(transaction, OnePondy.Domain.Payments.LedgerAccounts.GatewayClearing, -10m, null, "x", DateTimeOffset.UtcNow));
        var unknown = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)unknown.InnerException!).SqlState);
    }

    [DatabaseFact]
    public async Task Refunds_can_never_add_up_to_more_than_was_paid()
    {
        var bookingId = await NewBookingIdAsync();
        var now = DateTimeOffset.UtcNow;
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var attempt = OnePondy.Domain.Payments.PaymentAttempt.Create(bookingId, "order_" + Guid.NewGuid().ToString("N"), 1000m, "INR", now);
        attempt.MarkCaptured("pay_" + Guid.NewGuid().ToString("N"), now);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        db.Refunds.Add(OnePondy.Domain.Payments.Refund.Request(attempt.Id, bookingId, 600m, OnePondy.Domain.Payments.RefundReason.CustomerCancelled, now));
        await db.SaveChangesAsync();

        db.Refunds.Add(OnePondy.Domain.Payments.Refund.Request(attempt.Id, bookingId, 500m, OnePondy.Domain.Payments.RefundReason.CustomerCancelled, now));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("would exceed", ((PostgresException)failure.InnerException!).MessageText);
        db.ChangeTracker.Clear();

        // The rest of the money can still be refunded, and a failed refund frees its share again.
        var rest = OnePondy.Domain.Payments.Refund.Request(attempt.Id, bookingId, 400m, OnePondy.Domain.Payments.RefundReason.PartnerCancelled, now);
        db.Refunds.Add(rest);
        await db.SaveChangesAsync();
        rest.MarkFailed(now);
        await db.SaveChangesAsync();
        db.Refunds.Add(OnePondy.Domain.Payments.Refund.Request(attempt.Id, bookingId, 400m, OnePondy.Domain.Payments.RefundReason.PartnerCancelled, now));
        await db.SaveChangesAsync();
    }

    [DatabaseFact]
    public async Task A_settlement_must_add_up_and_a_booking_is_settled_once()
    {
        var bookingId = await NewBookingIdAsync();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var businessId = await db.Bookings.AsNoTracking().Where(b => b.Id == bookingId).Select(b => b.BusinessId).SingleAsync();
        var start = new DateOnly(2030, 1, 1);

        var bad = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO settlements (id, business_id, period_start, period_end, gross_amount, commission_amount, refund_amount, net_amount, status, created_at) VALUES ({Guid.NewGuid()}, {businessId}, {start}, {start}, 1000, 100, 0, 999, 'draft', now())"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, bad.SqlState);

        var settlement = OnePondy.Domain.Payments.Settlement.Draft(businessId, start, start.AddDays(6), 1000m, 100m, 0m, DateTimeOffset.UtcNow);
        db.Settlements.Add(settlement);
        db.SettlementItems.Add(OnePondy.Domain.Payments.SettlementItem.Create(settlement.Id, bookingId, OnePondy.Domain.Payments.SettlementItemKind.Sale, 1000m, 100m));
        await db.SaveChangesAsync();
        Assert.Equal(900m, (await db.Settlements.AsNoTracking().SingleAsync(s => s.Id == settlement.Id)).NetAmount);

        var second = OnePondy.Domain.Payments.Settlement.Draft(businessId, start.AddDays(7), start.AddDays(13), 1000m, 100m, 0m, DateTimeOffset.UtcNow);
        db.Settlements.Add(second);
        db.SettlementItems.Add(OnePondy.Domain.Payments.SettlementItem.Create(second.Id, bookingId, OnePondy.Domain.Payments.SettlementItemKind.Sale, 1000m, 100m));
        var twice = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)twice.InnerException!).SqlState);
    }

    // --- Phase 7 step 7.2: the payment-start endpoint and the cancellation/payment race, over HTTP and real PostgreSQL ---

    [DatabaseFact]
    public async Task A_customer_can_start_a_payment_and_asking_again_returns_the_same_order()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();

        var first = await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer);
        var firstBody = await ReadAsync(first);
        var second = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.StartsWith("order_it_", firstBody.GetProperty("orderId").GetString());
        Assert.Equal("rzp_test_integration", firstBody.GetProperty("keyId").GetString());
        Assert.Equal("sandbox", firstBody.GetProperty("mode").GetString());
        Assert.Equal("razorpay", firstBody.GetProperty("provider").GetString());
        Assert.Equal(50_000, firstBody.GetProperty("amountPaise").GetInt64());
        Assert.Equal("INR", firstBody.GetProperty("currency").GetString());
        Assert.Equal(firstBody.GetProperty("orderId").GetString(), second.GetProperty("orderId").GetString());
        Assert.Equal(1, gateway.OrdersCreated);
    }

    [DatabaseFact]
    public async Task Only_the_booking_customer_can_start_a_payment_and_a_booking_not_due_is_refused()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();
        var stranger = await SignInCustomerAsync(client);
        var (pendingCustomer, pendingId) = await NewBookingAsync();

        var anonymous = await client.PostAsync($"/api/v1/bookings/{bookingId}/payment", null);
        var notYours = await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", stranger);
        var notDue = await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{pendingId}/payment", pendingCustomer);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notYours.StatusCode);
        Assert.Equal("booking_not_found", await ErrorCodeAsync(notYours));
        Assert.Equal(HttpStatusCode.Conflict, notDue.StatusCode);
        Assert.Equal("payment_not_due", await ErrorCodeAsync(notDue));
        Assert.Equal(0, gateway.OrdersCreated);
        Assert.NotNull(customer);
    }

    [DatabaseFact]
    public async Task Without_gateway_keys_starting_a_payment_reports_payments_unavailable()
    {
        using var client = Factory.CreateClient();
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();

        var response = await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("payments_unavailable", await ErrorCodeAsync(response));
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal(0, await DbOf(scope).PaymentAttempts.AsNoTracking().CountAsync(a => a.BookingId == bookingId));
    }

    [DatabaseFact]
    public async Task Many_simultaneous_requests_to_pay_share_one_open_order()
    {
        var gateway = new FakePaymentGateway(TimeSpan.FromMilliseconds(150));
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer)));
        var orderIds = new HashSet<string?>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            orderIds.Add((await ReadAsync(response)).GetProperty("orderId").GetString());
        }

        Assert.Single(orderIds);
        await using var scope = Factory.Services.CreateAsyncScope();
        var open = await DbOf(scope).PaymentAttempts.AsNoTracking()
            .Where(a => a.BookingId == bookingId && a.Status == OnePondy.Domain.Payments.PaymentAttemptStatus.Created)
            .ToListAsync();
        Assert.Equal(orderIds.Single(), Assert.Single(open).ProviderOrderId);
    }

    [DatabaseFact]
    public async Task A_customer_can_cancel_while_waiting_to_pay_and_a_payment_that_still_arrives_is_refunded()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();
        var started = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer));
        var orderId = started.GetProperty("orderId").GetString()!;
        var paymentId = "pay_it_" + Guid.NewGuid().ToString("N");
        var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));

        var cancel = await SendAsync(
            client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", customer, new { version = current.GetProperty("version").GetUInt32(), reason = "Changed my mind" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("cancelled", (await ReadAsync(cancel)).GetProperty("status").GetString());

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var captured = await PaymentServiceFor(scope, gateway).RecordCaptureAsync(orderId, paymentId, 500m, default);
            Assert.Equal(CaptureOutcome.RefundRaised, captured.Value!.Outcome);
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            Assert.Equal(OnePondy.Domain.Bookings.BookingStatus.RefundPending, (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId)).Status);
            var refund = await db.Refunds.AsNoTracking().SingleAsync(r => r.BookingId == bookingId);
            Assert.Equal(500m, refund.Amount);
            Assert.Equal(OnePondy.Domain.Payments.RefundReason.LatePayment, refund.Reason);
            Assert.StartsWith("rfnd_it_", refund.ProviderRefundId);

            // The same webhook again changes nothing.
            var again = await PaymentServiceFor(scope, gateway).RecordCaptureAsync(orderId, paymentId, 500m, default);
            Assert.Equal(CaptureOutcome.AlreadyRecorded, again.Value!.Outcome);
            Assert.Equal(1, await db.Refunds.AsNoTracking().CountAsync(r => r.BookingId == bookingId));
        }

        Assert.Single(gateway.RefundRequests);
    }

    [DatabaseFact]
    public async Task A_payment_result_computed_from_stale_state_is_refused_and_the_retry_sees_the_cancellation()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();
        var started = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer));
        var orderId = started.GetProperty("orderId").GetString()!;
        var paymentId = "pay_it_" + Guid.NewGuid().ToString("N");

        // The payment handler has already read the booking as "waiting for payment" ...
        await using var stale = Factory.Services.CreateAsyncScope();
        await DbOf(stale).Bookings.SingleAsync(b => b.Id == bookingId);

        // ... when the customer cancels in another request.
        var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));
        await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", customer, new { version = current.GetProperty("version").GetUInt32(), reason = (string?)null });

        var raced = await PaymentServiceFor(stale, gateway).RecordCaptureAsync(orderId, paymentId, 500m, default);

        Assert.Equal("payment_busy", raced.Error!.Code);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            Assert.Equal(OnePondy.Domain.Bookings.BookingStatus.Cancelled, (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId)).Status);
            Assert.Equal(0, await db.Refunds.AsNoTracking().CountAsync(r => r.BookingId == bookingId));
            // The cancellation closed the order; the refused payment left it uncaptured.
            Assert.Equal(OnePondy.Domain.Payments.PaymentAttemptStatus.Expired, (await db.PaymentAttempts.AsNoTracking().SingleAsync(a => a.ProviderOrderId == orderId)).Status);
        }

        // The gateway retries the webhook; this time the cancellation is seen and the money goes back.
        await using (var retry = Factory.Services.CreateAsyncScope())
        {
            var result = await PaymentServiceFor(retry, gateway).RecordCaptureAsync(orderId, paymentId, 500m, default);
            Assert.Equal(CaptureOutcome.RefundRaised, result.Value!.Outcome);
        }

        Assert.Single(gateway.RefundRequests);
    }

    // --- Phase 7 step 7.3: the signed webhook, over HTTP and real PostgreSQL ---

    private static string PaymentCapturedJson(string orderId, string paymentId, long paise, string eventType = "payment.captured") =>
        JsonSerializer.Serialize(new
        {
            entity = "event",
            @event = eventType,
            payload = new
            {
                payment = new
                {
                    entity = new
                    {
                        id = paymentId,
                        order_id = orderId,
                        amount = paise,
                        currency = "INR",
                        status = "captured",
                        email = "customer@example.com",
                        contact = "+919876543210",
                    },
                },
            },
        });

    private static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string json,
        string? eventId,
        string? signature = null,
        string secret = WebhookSecret)
    {
        var body = System.Text.Encoding.UTF8.GetBytes(json);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Razorpay-Signature", signature ?? RazorpaySignature.Compute(secret, body));
        if (eventId is not null)
        {
            request.Headers.Add("X-Razorpay-Event-Id", eventId);
        }

        return await client.SendAsync(request);
    }

    private static string NewEventId() => "evt_it_" + Guid.NewGuid().ToString("N");

    /// <summary>A booking waiting for payment, with its gateway order already created through the real endpoint.</summary>
    private async Task<(string Customer, Guid BookingId, string OrderId)> BookingWithOpenOrderAsync(HttpClient client)
    {
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();
        var started = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer));
        return (customer, bookingId, started.GetProperty("orderId").GetString()!);
    }

    private async Task<string> BookingStatusAsync(Guid bookingId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return SnakeCaseText((await DbOf(scope).Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId)).Status.ToString());
    }

    private static string SnakeCaseText(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    [DatabaseFact]
    public async Task A_signed_payment_webhook_confirms_the_booking_and_a_repeat_changes_nothing()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var json = PaymentCapturedJson(orderId, "pay_" + Guid.NewGuid().ToString("N"), 50_000);
        var eventId = NewEventId();

        var first = await PostWebhookAsync(client, json, eventId);
        var second = await PostWebhookAsync(client, json, eventId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("processed", (await ReadAsync(first)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("duplicate", (await ReadAsync(second)).GetProperty("status").GetString());
        Assert.Equal("confirmed", await BookingStatusAsync(bookingId));

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        Assert.Equal(1, await db.GatewayEvents.AsNoTracking().CountAsync(e => e.ProviderEventId == eventId));
        var history = await db.Bookings.AsNoTracking().Where(b => b.Id == bookingId).SelectMany(b => b.History).CountAsync(h => h.NewStatus == OnePondy.Domain.Bookings.BookingStatus.Confirmed);
        Assert.Equal(1, history);
        Assert.Empty(gateway.RefundRequests);
    }

    [DatabaseFact]
    public async Task A_webhook_with_a_bad_signature_is_refused_and_leaves_no_trace()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var json = PaymentCapturedJson(orderId, "pay_" + Guid.NewGuid().ToString("N"), 50_000);
        var eventId = NewEventId();

        var wrong = await PostWebhookAsync(client, json, eventId, signature: "00" + new string('a', 62));
        var otherSecret = await PostWebhookAsync(client, json, eventId, secret: "someone-elses-secret");
        var noSignature = await client.PostAsync("/api/v1/webhooks/payments", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        foreach (var response in new[] { wrong, otherSecret, noSignature })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("webhook_signature_invalid", await ErrorCodeAsync(response));
        }

        Assert.Equal("awaiting_payment", await BookingStatusAsync(bookingId));
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal(0, await DbOf(scope).GatewayEvents.AsNoTracking().CountAsync(e => e.ProviderEventId == eventId));
    }

    [DatabaseFact]
    public async Task A_signed_body_that_was_altered_afterwards_is_refused()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var paymentId = "pay_" + Guid.NewGuid().ToString("N");
        var signedFor = System.Text.Encoding.UTF8.GetBytes(PaymentCapturedJson(orderId, paymentId, 50_000));
        var signature = RazorpaySignature.Compute(WebhookSecret, signedFor);

        var forged = await PostWebhookAsync(client, PaymentCapturedJson(orderId, paymentId, 1), NewEventId(), signature);

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal("awaiting_payment", await BookingStatusAsync(bookingId));
    }

    [DatabaseFact]
    public async Task A_webhook_without_an_event_id_is_refused_and_one_without_a_server_secret_is_unavailable()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, _, orderId) = await BookingWithOpenOrderAsync(client);
        var json = PaymentCapturedJson(orderId, "pay_" + Guid.NewGuid().ToString("N"), 50_000);

        var noId = await PostWebhookAsync(client, json, eventId: null);
        Assert.Equal(HttpStatusCode.BadRequest, noId.StatusCode);
        Assert.Equal("webhook_malformed", await ErrorCodeAsync(noId));

        // The shared test server has no webhook secret configured, so it cannot verify anything.
        using var unconfigured = Factory.CreateClient();
        var refused = await PostWebhookAsync(unconfigured, json, NewEventId());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("webhook_not_configured", await ErrorCodeAsync(refused));
    }

    [DatabaseFact]
    public async Task A_payment_the_customer_cancelled_before_it_landed_is_refunded_through_the_webhook()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));
        await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", customer, new { version = current.GetProperty("version").GetUInt32(), reason = (string?)null });
        var paymentId = "pay_" + Guid.NewGuid().ToString("N");

        var response = await PostWebhookAsync(client, PaymentCapturedJson(orderId, paymentId, 50_000), NewEventId());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("refund_pending", await BookingStatusAsync(bookingId));
        var sent = Assert.Single(gateway.RefundRequests);
        Assert.Equal(paymentId, sent.PaymentId);
        Assert.Equal(50_000, sent.AmountPaise);

        // Razorpay then reports the refund paid out.
        await using var scope = Factory.Services.CreateAsyncScope();
        var refundId = (await DbOf(scope).Refunds.AsNoTracking().SingleAsync(r => r.BookingId == bookingId)).ProviderRefundId!;
        var processed = JsonSerializer.Serialize(new { entity = "event", @event = "refund.processed", payload = new { refund = new { entity = new { id = refundId, payment_id = paymentId, amount = 50_000, status = "processed" } } } });
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, processed, NewEventId())).StatusCode);
        Assert.Equal("refunded", await BookingStatusAsync(bookingId));
    }

    [DatabaseFact]
    public async Task A_payment_webhook_for_an_unknown_order_is_acknowledged_and_recorded_for_follow_up()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var eventId = NewEventId();

        var response = await PostWebhookAsync(client, PaymentCapturedJson("order_not_ours_" + Guid.NewGuid().ToString("N"), "pay_" + Guid.NewGuid().ToString("N"), 50_000), eventId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("needsattention", (await ReadAsync(response)).GetProperty("status").GetString());
        await using var scope = Factory.Services.CreateAsyncScope();
        var stored = await DbOf(scope).GatewayEvents.AsNoTracking().SingleAsync(e => e.ProviderEventId == eventId);
        Assert.Equal("payment_unknown_order", stored.Error);
        Assert.DoesNotContain("customer@example.com", stored.Payload);
        Assert.DoesNotContain("9876543210", stored.Payload);
    }

    [DatabaseFact]
    public async Task The_same_webhook_delivered_many_times_at_once_confirms_the_booking_exactly_once()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var json = PaymentCapturedJson(orderId, "pay_" + Guid.NewGuid().ToString("N"), 50_000);
        var eventId = NewEventId();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostWebhookAsync(client, json, eventId)));

        // Each delivery either did the work, saw it done, or was told to try again; never a server error.
        foreach (var response in responses)
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                $"unexpected {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        // A retry after the burst settles on the duplicate answer.
        Assert.Equal("duplicate", (await ReadAsync(await PostWebhookAsync(client, json, eventId))).GetProperty("status").GetString());
        Assert.Equal("confirmed", await BookingStatusAsync(bookingId));
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        Assert.Equal(1, await db.GatewayEvents.AsNoTracking().CountAsync(e => e.ProviderEventId == eventId));
        Assert.Equal(1, await db.Bookings.AsNoTracking().Where(b => b.Id == bookingId).SelectMany(b => b.History).CountAsync(h => h.NewStatus == OnePondy.Domain.Bookings.BookingStatus.Confirmed));
        Assert.Equal(1, await db.PaymentAttempts.AsNoTracking().CountAsync(a => a.BookingId == bookingId && a.Status == OnePondy.Domain.Payments.PaymentAttemptStatus.Captured));
        Assert.Empty(gateway.RefundRequests);
    }

    // --- Phase 7 step 7.4: the Worker asks the gateway before it expires a booking waiting for payment ---

    [DatabaseFact]
    public async Task A_payment_the_webhook_never_reported_is_found_by_the_worker_and_the_booking_is_kept()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var paymentId = "pay_" + Guid.NewGuid().ToString("N");
        gateway.CustomerPaid(orderId, paymentId, 50_000);
        await MakeOverdueAsync(bookingId);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var result = await ExpiryFor(scope, gateway).ExpireDueHoldsAsync(default);
            Assert.True(result.Resolved >= 1);
        }

        Assert.Equal("confirmed", await BookingStatusAsync(bookingId));
        Assert.Equal(1, await ReservedForAsync(bookingId));
        await using var check = Factory.Services.CreateAsyncScope();
        var attempt = await DbOf(check).PaymentAttempts.AsNoTracking().SingleAsync(a => a.BookingId == bookingId);
        Assert.Equal(OnePondy.Domain.Payments.PaymentAttemptStatus.Captured, attempt.Status);
        Assert.Equal(paymentId, attempt.ProviderPaymentId);
        Assert.Empty(gateway.RefundRequests);
    }

    [DatabaseFact]
    public async Task An_unpaid_booking_is_expired_by_the_worker_its_order_closed_and_a_new_payment_refused()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId, _) = await BookingWithOpenOrderAsync(client);
        await MakeOverdueAsync(bookingId);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            Assert.True((await ExpiryFor(scope, gateway).ExpireDueHoldsAsync(default)).Expired >= 1);
        }

        Assert.Equal("expired", await BookingStatusAsync(bookingId));
        Assert.Equal(0, await ReservedForAsync(bookingId));
        await using (var check = Factory.Services.CreateAsyncScope())
        {
            var attempt = await DbOf(check).PaymentAttempts.AsNoTracking().SingleAsync(a => a.BookingId == bookingId);
            Assert.Equal(OnePondy.Domain.Payments.PaymentAttemptStatus.Expired, attempt.Status);
        }

        var retry = await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal("payment_not_due", await ErrorCodeAsync(retry));
    }

    [DatabaseFact]
    public async Task When_the_gateway_cannot_be_asked_the_worker_keeps_the_booking_for_now()
    {
        var gateway = new FakePaymentGateway { LookupsFail = true };
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, bookingId, _) = await BookingWithOpenOrderAsync(client);
        await MakeOverdueAsync(bookingId);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var result = await ExpiryFor(scope, gateway).ExpireDueHoldsAsync(default);
            Assert.True(result.Deferred >= 1);
        }

        Assert.Equal("awaiting_payment", await BookingStatusAsync(bookingId));
        Assert.Equal(1, await ReservedForAsync(bookingId));
    }

    [DatabaseFact]
    public async Task A_payment_arriving_just_as_the_worker_expires_the_booking_ends_one_consistent_way_every_time()
    {
        // Run the worker and the webhook at the same instant, repeatedly. The booking must end either paid and kept,
        // or expired with the money refunded in full: never confirmed and refunded, never expired and unrefunded.
        for (var round = 0; round < 6; round++)
        {
            var gateway = new FakePaymentGateway();
            await using var factory = WithFakeGateway(gateway);
            using var client = factory.CreateClient();
            var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
            var paymentId = "pay_" + Guid.NewGuid().ToString("N");
            var json = PaymentCapturedJson(orderId, paymentId, 50_000);
            var eventId = NewEventId();
            await MakeOverdueAsync(bookingId);

            // The payment is still in flight at the gateway when the worker looks, so the worker sees nothing paid.
            var worker = Task.Run(async () =>
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                await ExpiryFor(scope, gateway).ExpireDueHoldsAsync(default);
            });
            var webhook = PostWebhookAsync(client, json, eventId);
            await Task.WhenAll(worker, webhook);

            var answer = (await webhook).StatusCode;
            Assert.True(answer is HttpStatusCode.OK or HttpStatusCode.Conflict, $"round {round}: webhook answered {(int)answer}");

            // Razorpay redelivers anything that was not a 2xx.
            for (var retry = 0; retry < 5 && answer != HttpStatusCode.OK; retry++)
            {
                answer = (await PostWebhookAsync(client, json, eventId)).StatusCode;
            }

            Assert.Equal(HttpStatusCode.OK, answer);

            await using var scope2 = Factory.Services.CreateAsyncScope();
            var db = DbOf(scope2);
            var status = (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId)).Status;
            var refunds = await db.Refunds.AsNoTracking().Where(r => r.BookingId == bookingId).ToListAsync();
            var attempt = await db.PaymentAttempts.AsNoTracking().SingleAsync(a => a.BookingId == bookingId);

            Assert.Equal(OnePondy.Domain.Payments.PaymentAttemptStatus.Captured, attempt.Status);
            if (status == OnePondy.Domain.Bookings.BookingStatus.Confirmed)
            {
                Assert.Empty(refunds);
                Assert.Equal(1, await ReservedForAsync(bookingId));
            }
            else
            {
                Assert.Equal(OnePondy.Domain.Bookings.BookingStatus.Expired, status);
                Assert.Equal(500m, Assert.Single(refunds).Amount);
                Assert.Single(gateway.RefundRequests);
                Assert.Equal(0, await ReservedForAsync(bookingId));
            }
        }
    }

    // --- Phase 7 step 7.5: refunds and the ledger, over HTTP and real PostgreSQL ---

    /// <summary>A booking accepted, paid through a signed webhook, and so confirmed; with its ledger entries booked.</summary>
    private async Task<(string Customer, Guid BookingId, string PaymentId, string PartnerToken)> PaidBookingAsync(HttpClient client)
    {
        var (customer, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var paymentId = "pay_" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, PaymentCapturedJson(orderId, paymentId, 50_000), NewEventId())).StatusCode);
        Assert.Equal("confirmed", await BookingStatusAsync(bookingId));
        return (customer, bookingId, paymentId, string.Empty);
    }

    private static string RefundProcessedJson(string refundId, string paymentId, long paise) =>
        JsonSerializer.Serialize(new { entity = "event", @event = "refund.processed", payload = new { refund = new { entity = new { id = refundId, payment_id = paymentId, amount = paise, status = "processed" } } } });

    private async Task<decimal> LedgerBalanceAsync(Guid bookingId, string accountPrefix)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var entries = await DbOf(scope).LedgerEntries.AsNoTracking().Where(e => e.BookingId == bookingId).ToListAsync();
        return entries.Where(e => e.Account.StartsWith(accountPrefix, StringComparison.Ordinal)).Sum(e => e.Amount);
    }

    [DatabaseFact]
    public async Task A_payment_is_booked_to_the_ledger_once_even_when_the_webhook_is_repeated()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var json = PaymentCapturedJson(orderId, "pay_" + Guid.NewGuid().ToString("N"), 50_000);

        await PostWebhookAsync(client, json, NewEventId());
        await PostWebhookAsync(client, json, NewEventId()); // Razorpay also sends order.paid, with a different event id
        await PostWebhookAsync(client, json, NewEventId());

        Assert.Equal(500m, await LedgerBalanceAsync(bookingId, "gateway_clearing"));
        Assert.Equal(-50m, await LedgerBalanceAsync(bookingId, "platform_commission"));
        Assert.Equal(-450m, await LedgerBalanceAsync(bookingId, "partner_payable:"));
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal(3, await DbOf(scope).LedgerEntries.AsNoTracking().CountAsync(e => e.BookingId == bookingId));
    }

    [DatabaseFact]
    public async Task A_customer_cancelling_after_the_free_deadline_is_refunded_the_listings_percentage_and_the_books_follow()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway, refundsEnabled: true);
        using var client = factory.CreateClient();
        var (customer, bookingId, paymentId, _) = await PaidBookingAsync(client);
        var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));

        var cancel = await SendAsync(
            client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", customer, new { version = current.GetProperty("version").GetUInt32(), reason = "Plans changed" });

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("refundPending", (await ReadAsync(cancel)).GetProperty("status").GetString());
        Assert.Equal(0, await ReservedForAsync(bookingId));
        var sent = Assert.Single(gateway.RefundRequests);
        Assert.Equal(paymentId, sent.PaymentId);
        // The slot starts a day after the listing was made, so this cancellation is inside the 24-hour free window's end:
        // the listing's 50% applies, not the full price.
        Assert.Equal(25_000, sent.AmountPaise);

        string refundId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var refund = await DbOf(scope).Refunds.AsNoTracking().SingleAsync(r => r.BookingId == bookingId);
            Assert.Equal(250m, refund.Amount);
            Assert.Equal(OnePondy.Domain.Payments.RefundReason.CustomerCancelled, refund.Reason);
            refundId = refund.ProviderRefundId!;
        }

        // Razorpay reports the money paid out: twice, as a redelivery would.
        var processed = RefundProcessedJson(refundId, paymentId, 25_000);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, processed, NewEventId())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, processed, NewEventId())).StatusCode);

        // 500 paid, 250 handed back: half of each share stays booked, and nothing was booked twice.
        Assert.Equal("refunded", await BookingStatusAsync(bookingId));
        Assert.Equal(250m, await LedgerBalanceAsync(bookingId, "gateway_clearing"));
        Assert.Equal(-25m, await LedgerBalanceAsync(bookingId, "platform_commission"));
        Assert.Equal(-225m, await LedgerBalanceAsync(bookingId, "partner_payable:"));
        await using var check = Factory.Services.CreateAsyncScope();
        Assert.Equal(6, await DbOf(check).LedgerEntries.AsNoTracking().CountAsync(e => e.BookingId == bookingId));
    }

    [DatabaseFact]
    public async Task A_partner_cancelling_a_paid_booking_refunds_everything_and_the_books_return_to_zero()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway, refundsEnabled: true);
        using var client = factory.CreateClient();
        var (_, bookingId, paymentId, _) = await PaidBookingAsync(client);
        var partner = await PartnerOfAsync(bookingId);
        string refundId;

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
            var wired = Wire(db, gateway, TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { RefundsEnabled = true }));
            var cancelled = await wired.Bookings.CancelForBusinessAsync(booking.BusinessId, bookingId, booking.Version, partner, "Closed that day", default);
            Assert.True(cancelled.IsSuccess, cancelled.Error?.Code);
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var refund = await DbOf(scope).Refunds.AsNoTracking().SingleAsync(r => r.BookingId == bookingId);
            Assert.Equal(500m, refund.Amount);
            Assert.Equal(OnePondy.Domain.Payments.RefundReason.PartnerCancelled, refund.Reason);
            refundId = refund.ProviderRefundId!;
        }

        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, RefundProcessedJson(refundId, paymentId, 50_000), NewEventId())).StatusCode);

        Assert.Equal("refunded", await BookingStatusAsync(bookingId));
        Assert.Equal(0m, await LedgerBalanceAsync(bookingId, "gateway_clearing"));
        Assert.Equal(0m, await LedgerBalanceAsync(bookingId, "platform_commission"));
        Assert.Equal(0m, await LedgerBalanceAsync(bookingId, "partner_payable:"));
    }

    [DatabaseFact]
    public async Task While_refunds_are_off_a_paid_booking_cannot_be_cancelled_over_http()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId, _, _) = await PaidBookingAsync(client);
        var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));

        var cancel = await SendAsync(
            client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", customer, new { version = current.GetProperty("version").GetUInt32(), reason = (string?)null });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, cancel.StatusCode);
        Assert.Equal("refunds_not_enabled", await ErrorCodeAsync(cancel));
        Assert.Equal("confirmed", await BookingStatusAsync(bookingId));
        Assert.Equal(1, await ReservedForAsync(bookingId));
        Assert.Empty(gateway.RefundRequests);
    }

    [DatabaseFact]
    public async Task The_customer_and_the_partner_cancelling_at_the_same_moment_produce_exactly_one_refund()
    {
        for (var round = 0; round < 4; round++)
        {
            var gateway = new FakePaymentGateway();
            await using var factory = WithFakeGateway(gateway, refundsEnabled: true);
            using var client = factory.CreateClient();
            var (customer, bookingId, _, _) = await PaidBookingAsync(client);
            var partner = await PartnerOfAsync(bookingId);

            var customerTask = Task.Run(async () =>
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var version = (await DbOf(scope).Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId)).Version;
                return await Wire(DbOf(scope), gateway, TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { RefundsEnabled = true }))
                    .Bookings.CancelMineAsync(bookingId, CustomerIdOf(customer), "mine", version, default);
            });
            var partnerTask = Task.Run(async () =>
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var db = DbOf(scope);
                var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
                return await Wire(db, gateway, TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { RefundsEnabled = true }))
                    .Bookings.CancelForBusinessAsync(booking.BusinessId, bookingId, booking.Version, partner, "theirs", default);
            });
            var results = await Task.WhenAll(customerTask, partnerTask);

            Assert.Equal(1, results.Count(r => r.IsSuccess));
            await using var check = Factory.Services.CreateAsyncScope();
            var db2 = DbOf(check);
            Assert.Equal(1, await db2.Refunds.AsNoTracking().CountAsync(r => r.BookingId == bookingId));
            Assert.Single(gateway.RefundRequests);
            Assert.Equal(OnePondy.Domain.Bookings.BookingStatus.RefundPending, (await db2.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId)).Status);
            Assert.Equal(0, await ReservedForAsync(bookingId));
        }
    }

    private Guid CustomerIdOf(string accessToken) =>
        Guid.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(accessToken).Subject);

    [DatabaseFact]
    public async Task A_refund_that_never_reached_the_gateway_is_sent_later_by_the_sweep()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway, refundsEnabled: true);
        using var client = factory.CreateClient();
        var (customer, bookingId, _, _) = await PaidBookingAsync(client);
        var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));
        await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", customer, new { version = current.GetProperty("version").GetUInt32(), reason = (string?)null });

        // Make it look like the first attempt never got through, and like it was made a while ago.
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await DbOf(scope).Database.ExecuteSqlAsync($"UPDATE refunds SET provider_refund_id = NULL, requested_at = now() - interval '10 minutes' WHERE booking_id = {bookingId}");
        }

        var before = gateway.RefundRequests.Count;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var sent = await Wire(DbOf(scope), gateway, TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { RefundsEnabled = true }))
                .Refunds.SendUnsentAsync(50, default);
            Assert.True(sent >= 1);
        }

        // The sweep sends every old unsent refund in the shared test database, so at least ours went out.
        Assert.True(gateway.RefundRequests.Count >= before + 1);
        await using var check = Factory.Services.CreateAsyncScope();
        Assert.NotNull((await DbOf(check).Refunds.AsNoTracking().SingleAsync(r => r.BookingId == bookingId)).ProviderRefundId);
    }

    [DatabaseFact]
    public async Task The_ledger_refuses_a_second_line_for_the_same_account_in_a_transaction()
    {
        var transaction = Guid.NewGuid();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var now = DateTimeOffset.UtcNow;
        db.LedgerEntries.AddRange(
            OnePondy.Domain.Payments.LedgerEntry.Create(transaction, OnePondy.Domain.Payments.LedgerAccounts.GatewayClearing, 100m, null, "a", now),
            OnePondy.Domain.Payments.LedgerEntry.Create(transaction, OnePondy.Domain.Payments.LedgerAccounts.PlatformCommission, -100m, null, "b", now));
        await db.SaveChangesAsync();

        // Posting the same event again would double count it; the database refuses.
        db.LedgerEntries.AddRange(
            OnePondy.Domain.Payments.LedgerEntry.Create(transaction, OnePondy.Domain.Payments.LedgerAccounts.GatewayClearing, 100m, null, "a", now),
            OnePondy.Domain.Payments.LedgerEntry.Create(transaction, OnePondy.Domain.Payments.LedgerAccounts.PlatformCommission, -100m, null, "b", now));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());

        var postgres = failure as PostgresException ?? failure.InnerException as PostgresException;
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres!.SqlState);
    }

    // --- Phase 7 step 7.6: reconciliation, over HTTP and real PostgreSQL ---

    private async Task<Guid> SeedExceptionAsync(OnePondy.Domain.Payments.ReconciliationKind kind, string reference)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var exception = OnePondy.Domain.Payments.ReconciliationException.Detect(
            kind, $"it:{reference}", "Seeded by a test", DateTimeOffset.UtcNow, providerReference: reference, actualAmount: 12.5m);
        db.ReconciliationExceptions.Add(exception);
        await db.SaveChangesAsync();
        return exception.Id;
    }

    private static async Task<JsonElement> ListExceptionsAsync(HttpClient client, string admin, string query = "") =>
        await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/payments/exceptions?pageSize=100" + query, admin));

    [DatabaseFact]
    public async Task The_exceptions_table_refuses_duplicates_and_inconsistent_rows()
    {
        var reference = Guid.NewGuid().ToString("N");
        var id = await SeedExceptionAsync(OnePondy.Domain.Payments.ReconciliationKind.RefundStuck, reference);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);

        // The same problem is recorded once, whatever its status.
        db.ReconciliationExceptions.Add(OnePondy.Domain.Payments.ReconciliationException.Detect(
            OnePondy.Domain.Payments.ReconciliationKind.RefundStuck, $"it:{reference}", "again", DateTimeOffset.UtcNow));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)duplicate.InnerException!).SqlState);
        db.ChangeTracker.Clear();

        // Closed means it has a closing time; a person's resolution means a person and a note.
        var noTime = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE reconciliation_exceptions SET status = 'auto_resolved' WHERE id = {id}"));
        var noPerson = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE reconciliation_exceptions SET status = 'resolved', resolved_at = now() WHERE id = {id}"));
        var badKind = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE reconciliation_exceptions SET kind = 'made_up' WHERE id = {id}"));
        var badCount = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE reconciliation_exceptions SET occurrences = 0 WHERE id = {id}"));
        Assert.All(new[] { noTime, noPerson, badKind, badCount }, e => Assert.Equal(PostgresErrorCodes.CheckViolation, e.SqlState));
    }

    [DatabaseFact]
    public async Task Only_an_administrator_can_see_or_resolve_reconciliation_problems()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var customer = await SignInCustomerAsync(client);
        var id = await SeedExceptionAsync(OnePondy.Domain.Payments.ReconciliationKind.RefundFailed, Guid.NewGuid().ToString("N"));

        var anonymous = await client.GetAsync("/api/v1/admin/payments/exceptions");
        var asCustomer = await SendAsync(client, HttpMethod.Get, "/api/v1/admin/payments/exceptions", customer);
        var resolveAsCustomer = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/payments/exceptions/{id}/resolve", customer, new { note = "mine" });
        var asAdmin = await SendAsync(client, HttpMethod.Get, "/api/v1/admin/payments/exceptions", admin);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resolveAsCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
    }

    [DatabaseFact]
    public async Task The_list_can_be_filtered_and_an_administrator_resolves_a_problem_with_a_note()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var reference = "pay_" + Guid.NewGuid().ToString("N");
        var id = await SeedExceptionAsync(OnePondy.Domain.Payments.ReconciliationKind.RefundTotalMismatch, reference);

        var open = await ListExceptionsAsync(client, admin, "&status=open&kind=refundTotalMismatch");
        var ours = open.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("providerReference").GetString() == reference);
        Assert.Equal("open", ours.GetProperty("status").GetString());
        Assert.Equal("refundTotalMismatch", ours.GetProperty("kind").GetString());
        Assert.Equal(12.5m, ours.GetProperty("actualAmount").GetDecimal());
        Assert.All(open.GetProperty("items").EnumerateArray(), e => Assert.Equal("refundTotalMismatch", e.GetProperty("kind").GetString()));

        var noNote = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/payments/exceptions/{id}/resolve", admin, new { note = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);

        var resolved = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/payments/exceptions/{id}/resolve", admin, new { note = "Refunded by bank transfer" });
        var body = await ReadAsync(resolved);
        Assert.Equal("resolved", body.GetProperty("status").GetString());
        Assert.Equal("Refunded by bank transfer", body.GetProperty("resolutionNote").GetString());

        var again = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/payments/exceptions/{id}/resolve", admin, new { note = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("reconciliation_exception_closed", await ErrorCodeAsync(again));
        var unknown = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/payments/exceptions/{Guid.NewGuid()}/resolve", admin, new { note = "x" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var stillOpen = await ListExceptionsAsync(client, admin, "&status=open&kind=refundTotalMismatch");
        Assert.DoesNotContain(stillOpen.GetProperty("items").EnumerateArray(), e => e.GetProperty("providerReference").GetString() == reference);
        var count = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/payments/exceptions/open-count", admin));
        Assert.True(count.GetProperty("open").GetInt32() >= 0);
        var bad = await SendAsync(client, HttpMethod.Get, "/api/v1/admin/payments/exceptions?status=nonsense", admin);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [DatabaseFact]
    public async Task A_payment_whose_webhook_was_lost_is_found_by_reconciliation_and_shows_in_the_console()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var paymentId = "pay_" + Guid.NewGuid().ToString("N");
        gateway.CustomerPaid(orderId, paymentId, 50_000);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var options = Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = "rzp_test_integration" } });
            var summary = await Wire(DbOf(scope), gateway, TimeProvider.System, options).Reconciliation.RunFrequentChecksAsync(default);
            Assert.True(summary.AutoResolved >= 1);
        }

        Assert.Equal("confirmed", await BookingStatusAsync(bookingId));
        Assert.Equal(500m, await LedgerBalanceAsync(bookingId, "gateway_clearing"));
        var listed = await ListExceptionsAsync(client, admin, "&kind=paymentAppliedWithoutWebhook");
        var ours = listed.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("providerReference").GetString() == paymentId);
        Assert.Equal("autoResolved", ours.GetProperty("status").GetString());
        Assert.Equal(bookingId.ToString(), ours.GetProperty("bookingId").GetString());
    }

    [DatabaseFact]
    public async Task Two_reconciliation_runs_at_the_same_moment_apply_a_lost_payment_once_and_record_it_once()
    {
        for (var round = 0; round < 5; round++)
        {
            var gateway = new FakePaymentGateway();
            await using var factory = WithFakeGateway(gateway);
            using var client = factory.CreateClient();
            var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
            var paymentId = "pay_" + Guid.NewGuid().ToString("N");
            gateway.CustomerPaid(orderId, paymentId, 50_000);

            async Task RunAsync()
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var options = Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = "rzp_test_integration" } });
                await Wire(DbOf(scope), gateway, TimeProvider.System, options).Reconciliation.RunFrequentChecksAsync(default);
            }

            await Task.WhenAll(RunAsync(), RunAsync());
            await RunAsync(); // a third pass settles anything the first two left because they collided

            Assert.Equal("confirmed", await BookingStatusAsync(bookingId));
            await using var check = Factory.Services.CreateAsyncScope();
            var db = DbOf(check);
            Assert.Equal(3, await db.LedgerEntries.AsNoTracking().CountAsync(e => e.BookingId == bookingId));
            Assert.Equal(0, await db.Refunds.AsNoTracking().CountAsync(r => r.BookingId == bookingId));
            Assert.Equal(1, await db.ReconciliationExceptions.AsNoTracking().CountAsync(e => e.ProviderReference == paymentId));
            Assert.Equal(1, await db.PaymentAttempts.AsNoTracking().CountAsync(a => a.BookingId == bookingId && a.Status == OnePondy.Domain.Payments.PaymentAttemptStatus.Captured));
        }
    }

    [DatabaseFact]
    public async Task The_daily_check_lists_money_at_the_gateway_that_is_not_ours_and_remembers_how_far_it_got()
    {
        var gateway = new FakePaymentGateway();
        var stranger = "pay_" + Guid.NewGuid().ToString("N");
        gateway.ListedPayments.Add(new GatewayPaymentRecord(stranger, "order_" + Guid.NewGuid().ToString("N"), 123_400, 0, "INR", "captured"));
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var options = Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = "rzp_test_integration" } });

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var summary = await Wire(DbOf(scope), gateway, TimeProvider.System, options).Reconciliation.RunDailyAsync(day, default);
            Assert.True(summary.GatewayReachable);
            Assert.True(summary.Raised >= 1);
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var problem = await DbOf(scope).ReconciliationExceptions.AsNoTracking().SingleAsync(e => e.ProviderReference == stranger);
            Assert.Equal(OnePondy.Domain.Payments.ReconciliationKind.GatewayPaymentUnknown, problem.Kind);
            Assert.Equal(1234m, problem.ActualAmount);
        }

        // Running the day again records nothing new, only counts the sighting.
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await Wire(DbOf(scope), gateway, TimeProvider.System, options).Reconciliation.RunDailyAsync(day, default);
            var problem = await DbOf(scope).ReconciliationExceptions.AsNoTracking().SingleAsync(e => e.ProviderReference == stranger);
            Assert.Equal(2, problem.Occurrences);
        }

        // The system records how far the daily check has got, as itself rather than as a user.
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            await db.Database.ExecuteSqlAsync($"DELETE FROM app_settings WHERE key = 'payments_reconciled_through'");
            var days = await Wire(db, gateway, TimeProvider.System, options).Reconciliation.RunDueDailyChecksAsync(default);
            Assert.Equal(1, days);
        }

        await using var check = Factory.Services.CreateAsyncScope();
        var setting = await DbOf(check).AppSettings.AsNoTracking().SingleAsync(s => s.Key == "payments_reconciled_through");
        Assert.Equal(DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"), setting.Value);
        Assert.Null(setting.UpdatedByUserId);
    }

    // --- Phase 7 step 7.7: push notifications, over HTTP and real PostgreSQL ---

    private static string NewToken() => "ExponentPushToken[" + Guid.NewGuid().ToString("N") + "]";

    private static async Task<HttpResponseMessage> RegisterDeviceAsync(HttpClient client, string accessToken, string token, string platform = "android") =>
        await SendAsync(client, HttpMethod.Post, "/api/v1/devices", accessToken, new { token, platform });

    private OutboxDispatcher DispatcherFor(AsyncServiceScope scope, FakePushSender push, TimeProvider? clock = null) =>
        new(
            DbOf(scope),
            push,
            scope.ServiceProvider.GetRequiredService<OnePondy.Application.Identity.IUserAccountStore>(),
            clock ?? TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new NotificationOptions { Enabled = true }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxDispatcher>.Instance);

    /// <summary>Sends everything due. The test database holds other tests' messages too, so keep going until nothing is left to do.</summary>
    private async Task DrainAsync(FakePushSender push, TimeProvider? clock = null)
    {
        for (var pass = 0; pass < 40; pass++)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var done = await DispatcherFor(scope, push, clock).DispatchAsync(100, default);
            if (done.Sent + done.Discarded + done.Retrying == 0)
            {
                return;
            }
        }
    }

    [DatabaseFact]
    public async Task The_notification_tables_refuse_duplicates_and_inconsistent_rows()
    {
        using var client = Factory.CreateClient();
        var (customer, _) = await SignInWithPhoneAsync(client);
        var token = NewToken();
        Assert.Equal(HttpStatusCode.OK, (await RegisterDeviceAsync(client, customer, token)).StatusCode);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var device = await db.DeviceTokens.AsNoTracking().SingleAsync(d => d.Token == token);

        // One row per phone, and only known platforms.
        var duplicate = OnePondy.Domain.Notifications.DeviceToken.Register(device.UserId, device.SessionId, token, OnePondy.Domain.Notifications.DevicePlatform.Ios, DateTimeOffset.UtcNow);
        db.DeviceTokens.Add(duplicate);
        var twice = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)twice.InnerException!).SqlState);
        db.ChangeTracker.Clear();
        var badPlatform = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE device_tokens SET platform = 'windows' WHERE id = {device.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, badPlatform.SqlState);

        // A reminder is stored once.
        var key = "it:dedupe:" + Guid.NewGuid().ToString("N");
        db.OutboxMessages.Add(OnePondy.Domain.Payments.OutboxMessage.Create("booking.reminder", "{}", DateTimeOffset.UtcNow, key));
        await db.SaveChangesAsync();
        db.OutboxMessages.Add(OnePondy.Domain.Payments.OutboxMessage.Create("booking.reminder", "{}", DateTimeOffset.UtcNow, key));
        var again = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)again.InnerException!).SqlState);
    }

    [DatabaseFact]
    public async Task A_phone_is_registered_by_a_signed_in_user_and_the_same_phone_moves_to_whoever_signs_in_on_it_next()
    {
        using var client = Factory.CreateClient();
        var (first, _) = await SignInWithPhoneAsync(client);
        var (second, _) = await SignInWithPhoneAsync(client);
        var token = NewToken();

        var anonymous = await client.PostAsJsonAsync("/api/v1/devices", new { token, platform = "android" });
        var registered = await ReadAsync(await RegisterDeviceAsync(client, first, token));
        var again = await ReadAsync(await RegisterDeviceAsync(client, first, token));
        var bad = await RegisterDeviceAsync(client, first, "not-a-token");
        var badPlatform = await RegisterDeviceAsync(client, first, NewToken(), "windows");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(registered.GetProperty("id").GetString(), again.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badPlatform.StatusCode);

        // The same phone, now signed in as someone else: it belongs to them, not the previous user.
        await RegisterDeviceAsync(client, second, token);
        await using var scope = Factory.Services.CreateAsyncScope();
        var stored = await DbOf(scope).DeviceTokens.AsNoTracking().SingleAsync(d => d.Token == token);
        Assert.Equal(CustomerIdOf(second), stored.UserId);
    }

    [DatabaseFact]
    public async Task A_user_forgets_their_own_phone_and_cannot_forget_someone_elses()
    {
        using var client = Factory.CreateClient();
        var (owner, _) = await SignInWithPhoneAsync(client);
        var (stranger, _) = await SignInWithPhoneAsync(client);
        var id = (await ReadAsync(await RegisterDeviceAsync(client, owner, NewToken()))).GetProperty("id").GetString();

        var byStranger = await SendAsync(client, HttpMethod.Delete, $"/api/v1/devices/{id}", stranger);
        var byOwner = await SendAsync(client, HttpMethod.Delete, $"/api/v1/devices/{id}", owner);
        var twice = await SendAsync(client, HttpMethod.Delete, $"/api/v1/devices/{id}", owner);

        Assert.Equal(HttpStatusCode.NotFound, byStranger.StatusCode);
        Assert.Equal("device_not_found", await ErrorCodeAsync(byStranger));
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, twice.StatusCode);
    }

    [DatabaseFact]
    public async Task A_booking_that_is_requested_and_then_paid_notifies_each_side_once_on_their_own_phone()
    {
        var push = new FakePushSender();
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        var customerPhone = NewToken();
        await RegisterDeviceAsync(client, customer, customerPhone);
        var partnerId = await PartnerOfAsync(bookingId);
        var partnerPhone = NewToken();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            // The partner's phone, signed in on a session of their own.
            var session = OnePondy.Domain.Identity.UserSession.Start(partnerId, "phone", DateTimeOffset.UtcNow, TimeSpan.FromDays(1));
            var db = DbOf(scope);
            db.UserSessions.Add(session);
            await db.SaveChangesAsync();
            Assert.True((await new DeviceService(db, TimeProvider.System).RegisterAsync(partnerId, session.Id, partnerPhone, "android", default)).IsSuccess);
        }

        // Razorpay reports the payment; the webhook confirms the booking and queues the notifications in the same save.
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(client, PaymentCapturedJson(orderId, "pay_" + Guid.NewGuid().ToString("N"), 50_000), NewEventId())).StatusCode);
        await DrainAsync(push);

        var toCustomer = push.Sent.Where(m => m.To == customerPhone && m.BookingId == bookingId).ToList();
        var toPartner = push.Sent.Where(m => m.To == partnerPhone && m.BookingId == bookingId).ToList();
        Assert.Equal(OnePondy.Domain.Notifications.NotificationTypes.BookingConfirmed, Assert.Single(toCustomer).Type);
        Assert.Contains(toPartner, m => m.Type == OnePondy.Domain.Notifications.NotificationTypes.BookingPaid);
        Assert.DoesNotContain(toCustomer, m => m.Type == OnePondy.Domain.Notifications.NotificationTypes.BookingPaid);
        Assert.All(push.Sent.Where(m => m.BookingId == bookingId), m => Assert.DoesNotContain("customer@example.com", m.Body));

        // Sending the same queue again sends nothing more.
        var before = push.Sent.Count;
        await DrainAsync(push);
        Assert.Equal(before, push.Sent.Count);
    }

    [DatabaseFact]
    public async Task Signing_out_stops_notifications_to_that_phone_with_nothing_for_the_app_to_do()
    {
        var push = new FakePushSender();
        using var client = Factory.CreateClient();
        var (customer, bookingId) = await NewBookingAsync(); // a request: the partner is told, then the customer is told when it is rejected
        var phone = NewToken();
        Assert.Equal(HttpStatusCode.OK, (await RegisterDeviceAsync(client, customer, phone)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, "/api/v1/auth/logout", customer)).StatusCode);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = DbOf(scope);
            var partner = await PartnerOfAsync(bookingId);
            var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
            var wired = Wire(db, new FakePaymentGateway(), TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new PaymentsOptions()));
            Assert.True((await wired.Bookings.RejectAsync(booking.BusinessId, bookingId, booking.Version, partner, "Full", default)).IsSuccess);
        }

        await DrainAsync(push);

        Assert.DoesNotContain(push.Sent, m => m.To == phone);
    }

    [DatabaseFact]
    public async Task Deleting_an_account_removes_its_phones()
    {
        using var client = Factory.CreateClient();
        var (customer, phone) = await SignInWithPhoneAsync(client);
        var userId = CustomerIdOf(customer);
        Assert.Equal(HttpStatusCode.OK, (await RegisterDeviceAsync(client, customer, NewToken())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegisterDeviceAsync(client, customer, NewToken(), "ios")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await TryDeleteAccountAsync(client, customer, phone)).StatusCode);

        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.False(await DbOf(scope).DeviceTokens.AnyAsync(d => d.UserId == userId));
    }

    [DatabaseFact]
    public async Task Ending_a_session_in_the_database_takes_its_phones_with_it()
    {
        using var client = Factory.CreateClient();
        var (customer, _) = await SignInWithPhoneAsync(client);
        var token = NewToken();
        await RegisterDeviceAsync(client, customer, token);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        var sessionId = (await db.DeviceTokens.AsNoTracking().SingleAsync(d => d.Token == token)).SessionId;

        // The retention job deletes old sessions; their phones go with them.
        await db.Database.ExecuteSqlAsync($"DELETE FROM user_sessions WHERE id = {sessionId}");

        Assert.False(await db.DeviceTokens.AsNoTracking().AnyAsync(d => d.Token == token));
    }

    [DatabaseFact]
    public async Task Two_dispatchers_at_the_same_moment_send_a_message_exactly_once()
    {
        for (var round = 0; round < 5; round++)
        {
            var push = new FakePushSender();
            using var client = Factory.CreateClient();
            var (customer, bookingId) = await NewBookingAsync();
            var partner = await PartnerOfAsync(bookingId);
            var phone = NewToken();
            await using (var setup = Factory.Services.CreateAsyncScope())
            {
                var db = DbOf(setup);
                var session = OnePondy.Domain.Identity.UserSession.Start(partner, "phone", DateTimeOffset.UtcNow, TimeSpan.FromDays(1));
                db.UserSessions.Add(session);
                await db.SaveChangesAsync();
                await new DeviceService(db, TimeProvider.System).RegisterAsync(partner, session.Id, phone, "android", default);
            }

            async Task RunAsync()
            {
                for (var pass = 0; pass < 10; pass++)
                {
                    await using var scope = Factory.Services.CreateAsyncScope();
                    var done = await DispatcherFor(scope, push).DispatchAsync(100, default);
                    if (done.Sent + done.Discarded + done.Retrying == 0)
                    {
                        return;
                    }
                }
            }

            await Task.WhenAll(RunAsync(), RunAsync());

            Assert.Equal(1, push.Sent.Count(m => m.To == phone && m.BookingId == bookingId));
        }
    }

    [DatabaseFact]
    public async Task Two_reminder_runs_at_the_same_moment_queue_one_reminder()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (_, bookingId, orderId) = await BookingWithOpenOrderAsync(client);
        await PostWebhookAsync(client, PaymentCapturedJson(orderId, "pay_" + Guid.NewGuid().ToString("N"), 50_000), NewEventId());
        // The slot starts a day from when the listing was made, so this booking is due its reminder.
        async Task RunAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = DbOf(scope);
            await new BookingReminders(db, new NotificationOutbox(db, TimeProvider.System), TimeProvider.System).EnqueueDueAsync(default);
        }

        await Task.WhenAll(RunAsync(), RunAsync());
        await RunAsync();

        await using var check = Factory.Services.CreateAsyncScope();
        var count = await DbOf(check).OutboxMessages.AsNoTracking().CountAsync(m => m.DedupeKey == "booking.reminder:" + bookingId);
        Assert.Equal(1, count);
    }

    [DatabaseFact]
    public async Task Old_notification_records_are_purged_but_a_message_a_recent_log_still_points_at_is_kept()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        using var client = Factory.CreateClient();
        var (customer, _) = await SignInWithPhoneAsync(client);
        var userId = CustomerIdOf(customer);
        var deviceId = Guid.Parse((await ReadAsync(await RegisterDeviceAsync(client, customer, NewToken()))).GetProperty("id").GetString()!);
        var old = DateTimeOffset.UtcNow.AddDays(-40);
        var recent = DateTimeOffset.UtcNow.AddDays(-1);

        var plain = OnePondy.Domain.Payments.OutboxMessage.Create("booking.confirmed", "{}", old);
        plain.MarkDispatched(old);
        var pointedAt = OnePondy.Domain.Payments.OutboxMessage.Create("booking.confirmed", "{}", old);
        pointedAt.MarkDispatched(old);
        db.OutboxMessages.AddRange(plain, pointedAt);
        await db.SaveChangesAsync();
        db.NotificationLog.AddRange(
            OnePondy.Domain.Notifications.NotificationLogEntry.Refused(plain.Id, userId, deviceId, "booking.confirmed", "x", old),
            OnePondy.Domain.Notifications.NotificationLogEntry.Refused(pointedAt.Id, userId, deviceId, "booking.confirmed", "x", recent));
        await db.SaveChangesAsync();

        await new NotificationRetention(db, TimeProvider.System).PurgeAsync(default);

        await using var check = Factory.Services.CreateAsyncScope();
        var verify = DbOf(check);
        Assert.False(await verify.OutboxMessages.AnyAsync(m => m.Id == plain.Id));
        Assert.True(await verify.OutboxMessages.AnyAsync(m => m.Id == pointedAt.Id));
        Assert.False(await verify.NotificationLog.AnyAsync(l => l.OutboxMessageId == plain.Id));
        Assert.True(await verify.NotificationLog.AnyAsync(l => l.OutboxMessageId == pointedAt.Id));
    }

    [DatabaseFact]
    public async Task Outbox_messages_are_stored_with_their_payload()
    {
        var message = OnePondy.Domain.Payments.OutboxMessage.Create("booking.accepted", """{"bookingId":"x"}""", DateTimeOffset.UtcNow);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = DbOf(scope);
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();

        var stored = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);

        Assert.Null(stored.DispatchedAt);
        Assert.Contains("bookingId", stored.Payload);
    }

    // --- Phase 7 step 7.8: the app asks "is it paid yet?" and shows a receipt ---

    [DatabaseFact]
    public async Task Refreshing_asks_the_gateway_and_confirms_a_paid_booking_with_its_receipt()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();
        var started = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer));
        var paymentId = "pay_it_" + Guid.NewGuid().ToString("N");

        var beforePaying = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment/refresh", customer));
        gateway.CustomerPaid(started.GetProperty("orderId").GetString()!, paymentId, 50_000);
        var afterPaying = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment/refresh", customer));
        var again = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment/refresh", customer));

        Assert.Equal("awaitingPayment", beforePaying.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, beforePaying.GetProperty("payment").ValueKind);
        Assert.Equal("confirmed", afterPaying.GetProperty("status").GetString());
        var receipt = afterPaying.GetProperty("payment");
        Assert.Equal(500m, receipt.GetProperty("amount").GetDecimal());
        Assert.Equal("INR", receipt.GetProperty("currency").GetString());
        Assert.Equal(paymentId, receipt.GetProperty("reference").GetString());
        Assert.Equal(0, afterPaying.GetProperty("refunds").GetArrayLength());

        // Asking again changes nothing: the payment is recorded once.
        Assert.Equal("confirmed", again.GetProperty("status").GetString());
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await DbOf(scope).PaymentAttempts.AsNoTracking()
            .CountAsync(a => a.BookingId == bookingId && a.Status == OnePondy.Domain.Payments.PaymentAttemptStatus.Captured));
    }

    [DatabaseFact]
    public async Task Only_the_booking_customer_can_refresh_a_payment()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();
        var stranger = await SignInCustomerAsync(client);

        var anonymous = await client.PostAsync($"/api/v1/bookings/{bookingId}/payment/refresh", null);
        var notYours = await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment/refresh", stranger);
        var yours = await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment/refresh", customer);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notYours.StatusCode);
        Assert.Equal("booking_not_found", await ErrorCodeAsync(notYours));
        Assert.Equal(HttpStatusCode.OK, yours.StatusCode);
    }

    [DatabaseFact]
    public async Task Refreshing_a_booking_that_is_not_waiting_for_payment_just_returns_it()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId) = await NewBookingAsync();

        var response = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment/refresh", customer));

        Assert.Equal("pending", response.GetProperty("status").GetString());
        Assert.Equal(0, gateway.OrdersCreated);
    }

    [DatabaseFact]
    public async Task A_booking_shows_the_refund_raised_for_a_payment_that_arrived_after_cancelling()
    {
        var gateway = new FakePaymentGateway();
        await using var factory = WithFakeGateway(gateway);
        using var client = factory.CreateClient();
        var (customer, bookingId) = await AwaitingPaymentBookingAsync();
        var started = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/payment", customer));
        var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));
        await SendAsync(client, HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel", customer, new { version = current.GetProperty("version").GetUInt32() });

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await PaymentServiceFor(scope, gateway).RecordCaptureAsync(
                started.GetProperty("orderId").GetString()!, "pay_it_" + Guid.NewGuid().ToString("N"), 500m, default);
        }

        var detail = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/bookings/{bookingId}", customer));

        var refund = Assert.Single(detail.GetProperty("refunds").EnumerateArray());
        Assert.Equal(500m, refund.GetProperty("amount").GetDecimal());
        Assert.Equal("latePayment", refund.GetProperty("reason").GetString());
        Assert.Equal("refundPending", detail.GetProperty("status").GetString());
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

    /// <summary>The machine-readable <c>code</c> of an error reply, without insisting the reply succeeded.</summary>
    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
