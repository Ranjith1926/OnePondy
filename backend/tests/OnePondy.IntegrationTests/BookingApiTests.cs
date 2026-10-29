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
            var expiredCount = await new BookingHoldExpiry(db, new BookingService(db, later, Microsoft.Extensions.Options.Options.Create(new OnePondy.Application.Payments.PaymentsOptions())), later).ExpireDueHoldsAsync(default);
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
    private WebApplicationFactory<Program> WithFakeGateway(FakePaymentGateway gateway) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Payments:Razorpay:KeyId", "rzp_test_integration");
            builder.UseSetting("Payments:Razorpay:KeySecret", "integration-secret");
            builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(gateway));
        });

    private PaymentService PaymentServiceFor(AsyncServiceScope scope, FakePaymentGateway gateway) => new(
        DbOf(scope),
        gateway,
        TimeProvider.System,
        Microsoft.Extensions.Options.Options.Create(new PaymentsOptions { Razorpay = new RazorpayOptions { KeyId = "rzp_test_integration" } }),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentService>.Instance);

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
            var service = new BookingService(db, TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new OnePondy.Application.Payments.PaymentsOptions()));
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
            Assert.Equal(OnePondy.Domain.Payments.PaymentAttemptStatus.Created, (await db.PaymentAttempts.AsNoTracking().SingleAsync(a => a.ProviderOrderId == orderId)).Status);
        }

        // The gateway retries the webhook; this time the cancellation is seen and the money goes back.
        await using (var retry = Factory.Services.CreateAsyncScope())
        {
            var result = await PaymentServiceFor(retry, gateway).RecordCaptureAsync(orderId, paymentId, 500m, default);
            Assert.Equal(CaptureOutcome.RefundRaised, result.Value!.Outcome);
        }

        Assert.Single(gateway.RefundRequests);
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
