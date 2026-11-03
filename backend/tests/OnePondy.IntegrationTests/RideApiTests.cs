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
/// Rides (spec Phase 9, steps 9.1 and 9.2), against a real, migrated PostgreSQL database: the HTTP flow from a driver's application to a finished
/// trip, access control, two drivers accepting at the same moment, and the rules only the database can enforce. Runs only when
/// ONEPONDY_TEST_DATABASE is set (see README). Tests in this class run one after another and each clears the map of drivers first, because drivers on
/// duty are shared by the whole database.
/// </summary>
public sealed class RideApiTests : IAsyncLifetime
{
    private readonly Dictionary<string, string> _phones = [];

    private const double PromenadeLatitude = 11.9340;
    private const double PromenadeLongitude = 79.8350;
    private const double AurovilleLatitude = 12.0053;
    private const double AurovilleLongitude = 79.8100;

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
            // Rides are off in every real environment until the owner is ready, and other tests may look at the module list.
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                await DbOf(scope).Database.ExecuteSqlRawAsync("UPDATE module_flags SET status = 'coming_soon' WHERE key = 'rides'");
            }

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

    private async Task<(string Token, string UserId)> SignInAsync(HttpClient client, string? name = null)
    {
        var phone = ApiFactory.NewIndianMobile();
        var requested = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = phone });
        var challengeId = (await ReadAsync(requested)).GetProperty("challengeId").GetString();
        var verified = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId, code = Factory.Codes.For("+91" + phone) });
        var token = (await ReadAsync(verified)).GetProperty("accessToken").GetString()!;
        _phones[token] = phone;
        if (name is not null)
        {
            await ReadAsync(await SendAsync(client, HttpMethod.Patch, "/api/v1/profile", token, new { displayName = name }));
        }

        var profile = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/profile", token));
        return (token, profile.GetProperty("id").GetString()!);
    }

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

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return response.StatusCode == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static uint VersionOf(JsonElement element) => element.GetProperty("version").GetUInt32();

    private static string TodayPlusMonths(int months) => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5)).AddMonths(months).ToString("yyyy-MM-dd");

    private static object Application(string plate, string licence, string vehicleClass = "auto", uint? version = null) => new
    {
        fullName = "Ravi Kumar",
        vehicleClass,
        vehicleNumber = plate,
        licenceNumber = licence,
        permitNumber = "PERMIT" + Random.Shared.Next(100000, 999999),
        insurancePolicyNumber = "POLICY" + Random.Shared.Next(100000, 999999),
        insuranceValidUntil = TodayPlusMonths(6),
        version,
    };

    private static string NewPlate() => "PY01" + (char)('A' + Random.Shared.Next(26)) + (char)('A' + Random.Shared.Next(26)) + Random.Shared.Next(1000, 9999);

    private static string NewLicence() => "PY" + Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999);

    private async Task TurnRidesAsync(HttpClient client, string admin, string status)
    {
        var response = await SendAsync(client, HttpMethod.Put, "/api/v1/admin/config/modules/rides", admin, new { status });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Nobody is on duty: drivers on duty are shared by the whole database, so each test starts with an empty map.</summary>
    private async Task ClearTheMapAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await DbOf(scope).Database.ExecuteSqlRawAsync(
            "UPDATE ride_driver_profiles SET is_on_duty = false, on_duty_since = NULL");
    }

    private async Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client, string token)
    {
        var requested = await SendAsync(client, HttpMethod.Post, "/api/v1/account/deletion/code", token);
        var challengeId = (await ReadAsync(requested)).GetProperty("challengeId").GetString();
        return await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/account/deletion/confirm",
            token,
            new { challengeId, code = Factory.Codes.For("+91" + _phones[token]) });
    }

    private static OnePondyDbContext DbOf(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();

    /// <summary>A ride driver who applied, was approved, went on duty and shared a position at the given point.</summary>
    private async Task<(string Token, string UserId)> OnDutyDriverAsync(
        HttpClient client,
        string admin,
        string vehicleClass = "auto",
        double latitude = PromenadeLatitude,
        double longitude = PromenadeLongitude)
    {
        var (token, userId) = await SignInAsync(client, "Ravi Kumar");
        var applied = await ReadAsync(await SendAsync(client, HttpMethod.Put, "/api/v1/ride-driver/me", token, Application(NewPlate(), NewLicence(), vehicleClass)));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/ride-drivers/{userId}/approve", admin, new { version = VersionOf(applied) }));
        var approved = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/ride-driver/me", token));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/ride-driver/duty", token, new { onDuty = true, version = VersionOf(approved) }));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/ride-driver/location", token, new { latitude, longitude }));
        return (token, userId);
    }

    private static object Trip() => new
    {
        pickupLatitude = PromenadeLatitude,
        pickupLongitude = PromenadeLongitude,
        pickupLabel = "Promenade Beach",
        destinationLatitude = AurovilleLatitude,
        destinationLongitude = AurovilleLongitude,
        destinationLabel = "Auroville",
    };

    private static async Task<JsonElement> RequestRideAsync(HttpClient client, string rider, string vehicleClass = "auto")
    {
        var quotes = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/rides/quotes", rider, Trip()));
        var quote = quotes.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("vehicleClass").GetString() == vehicleClass);
        return await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/rides", rider, new { quoteId = quote.GetProperty("quoteId").GetString(), pickupNote = "Blue gate" }));
    }

    // --- The whole trip over HTTP ---

    [DatabaseFact]
    public async Task A_ride_goes_from_a_drivers_application_to_a_finished_cash_trip()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        await TurnRidesAsync(client, admin, "available");
        var (driver, driverId) = await OnDutyDriverAsync(client, admin);
        var (rider, riderId) = await SignInAsync(client, "Anita Raman");

        var ride = await RequestRideAsync(client, rider);
        var id = ride.GetProperty("id").GetString()!;
        var fare = ride.GetProperty("fare").GetDecimal();
        Assert.Equal("requested", ride.GetProperty("status").GetString());

        // The driver sees an offer with the name of the pickup and nothing that identifies the rider or the exact point.
        var home = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/ride-driver/rides", driver));
        var offer = home.GetProperty("offers").EnumerateArray().Single(o => o.GetProperty("id").GetString() == id);
        Assert.Equal("Promenade Beach", offer.GetProperty("pickupLabel").GetString());
        Assert.DoesNotContain("Anita", offer.GetRawText());
        Assert.DoesNotContain("Blue gate", offer.GetRawText());
        Assert.DoesNotContain("11.934", offer.GetRawText());

        var accepted = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/accept", driver));
        Assert.Equal("Anita", accepted.GetProperty("riderFirstName").GetString());
        Assert.Equal("Blue gate", accepted.GetProperty("pickupNote").GetString());

        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/location", driver, new { latitude = 11.95, longitude = 79.83 }));
        var current = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/rides/current", rider));
        Assert.Equal("accepted", current.GetProperty("status").GetString());
        Assert.Equal("Ravi", current.GetProperty("driver").GetProperty("firstName").GetString());
        Assert.Equal(11.95, current.GetProperty("driverLocation").GetProperty("latitude").GetDouble());
        Assert.DoesNotContain(driverId, current.GetRawText());

        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/arrive", driver));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/start", driver));
        var wrong = await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/complete", driver, new { cashCollected = fare - 1 });
        Assert.False(wrong.IsSuccessStatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(wrong));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/complete", driver, new { cashCollected = fare }));

        var done = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/rides/{id}", rider));
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, done.GetProperty("driverLocation").ValueKind);

        // The driver is free again, and the ride is in the rider's history.
        var after = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/ride-driver/rides", driver));
        Assert.Equal(JsonValueKind.Null, after.GetProperty("current").ValueKind);
        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/rides", rider));
        Assert.Equal(id, list.GetProperty("items")[0].GetProperty("id").GetString());

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var sql = new NpgsqlConnection(DbOf(scope).Database.GetConnectionString());
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = "SELECT kind, title, status, total_amount FROM customer_history WHERE customer_user_id = @rider";
        command.Parameters.AddWithValue("rider", Guid.Parse(riderId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "the ride is in the customer history");
        Assert.Equal("ride", reader.GetString(0));
        Assert.Equal("Promenade Beach to Auroville", reader.GetString(1));
        Assert.Equal("completed", reader.GetString(2));
        Assert.Equal(fare, reader.GetDecimal(3));
    }

    [DatabaseFact]
    public async Task Asking_for_a_fare_or_a_ride_needs_the_rides_module_to_be_on()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await TurnRidesAsync(client, admin, "comingSoon");
        var (rider, _) = await SignInAsync(client);

        var quote = await SendAsync(client, HttpMethod.Post, "/api/v1/rides/quotes", rider, Trip());
        var request = await SendAsync(client, HttpMethod.Post, "/api/v1/rides", rider, new { quoteId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Conflict, quote.StatusCode);
        Assert.Equal("rides_unavailable", await ErrorCodeAsync(quote));
        Assert.Equal("rides_unavailable", await ErrorCodeAsync(request));
    }

    [DatabaseFact]
    public async Task Ride_routes_need_a_sign_in_and_reviewing_drivers_needs_an_administrator()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (rider, riderId) = await SignInAsync(client);

        foreach (var path in new[] { "/api/v1/rides/current", "/api/v1/rides", "/api/v1/ride-driver/me", "/api/v1/ride-driver/rides", "/api/v1/admin/ride-drivers" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/ride-drivers", rider)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/ride-drivers/{riderId}/approve", rider, new { version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/ride-drivers", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Get, "/api/v1/rides/current", rider)).StatusCode);
    }

    [DatabaseFact]
    public async Task A_driver_who_is_not_approved_gets_no_offers_and_cannot_accept()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        await TurnRidesAsync(client, admin, "available");
        await OnDutyDriverAsync(client, admin);
        var (rider, _) = await SignInAsync(client);
        var ride = await RequestRideAsync(client, rider);
        var (pending, _) = await SignInAsync(client);
        await ReadAsync(await SendAsync(client, HttpMethod.Put, "/api/v1/ride-driver/me", pending, Application(NewPlate(), NewLicence())));

        var offers = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/ride-driver/rides", pending));
        var accept = await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{ride.GetProperty("id").GetString()}/accept", pending);

        Assert.False(offers.GetProperty("available").GetBoolean());
        Assert.Equal(0, offers.GetProperty("offers").GetArrayLength());
        Assert.Equal("ride_driver_not_available", await ErrorCodeAsync(accept));
    }

    // --- Two drivers at once ---

    [DatabaseFact]
    public async Task When_two_drivers_accept_the_same_ride_at_the_same_moment_exactly_one_gets_it()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        await TurnRidesAsync(client, admin, "available");
        var (first, firstId) = await OnDutyDriverAsync(client, admin);
        var (second, secondId) = await OnDutyDriverAsync(client, admin);

        for (var round = 0; round < 5; round++)
        {
            var (rider, _) = await SignInAsync(client);
            var ride = await RequestRideAsync(client, rider);
            var id = ride.GetProperty("id").GetString()!;

            var responses = await Task.WhenAll(
                SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/accept", first),
                SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/accept", second));

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
            Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            Assert.Equal("ride_taken", await ErrorCodeAsync(loser));

            await using var scope = Factory.Services.CreateAsyncScope();
            var holder = await DbOf(scope).Rides.AsNoTracking().Where(r => r.Id == Guid.Parse(id)).Select(r => r.DriverUserId).SingleAsync();
            Assert.Contains(holder, new Guid?[] { Guid.Parse(firstId), Guid.Parse(secondId) });

            // Finish the ride so both drivers are free for the next round.
            var winner = responses[0].StatusCode == HttpStatusCode.OK ? first : second;
            await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/arrive", winner));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/start", winner));
            await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/complete", winner, new { cashCollected = ride.GetProperty("fare").GetDecimal() }));
        }
    }

    [DatabaseFact]
    public async Task One_driver_accepting_two_rides_at_the_same_moment_gets_only_one()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        await TurnRidesAsync(client, admin, "available");
        var (driver, driverId) = await OnDutyDriverAsync(client, admin);
        var (riderA, _) = await SignInAsync(client);
        var (riderB, _) = await SignInAsync(client);
        var a = (await RequestRideAsync(client, riderA)).GetProperty("id").GetString();
        var b = (await RequestRideAsync(client, riderB)).GetProperty("id").GetString();

        var responses = await Task.WhenAll(
            SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{a}/accept", driver),
            SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{b}/accept", driver));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Contains(await ErrorCodeAsync(responses.Single(r => r.StatusCode != HttpStatusCode.OK)), new[] { "ride_driver_busy", "ride_taken" });
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await DbOf(scope).Rides.CountAsync(r => r.DriverUserId == Guid.Parse(driverId)));
    }

    [DatabaseFact]
    public async Task A_rider_cannot_have_two_open_rides_even_when_both_requests_arrive_together()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        await TurnRidesAsync(client, admin, "available");
        await OnDutyDriverAsync(client, admin);
        var (rider, riderId) = await SignInAsync(client);
        var quotes = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/rides/quotes", rider, Trip()));
        var autoQuote = quotes.GetProperty("options")[0].GetProperty("quoteId").GetString();
        var again = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/rides/quotes", rider, Trip()));
        var secondQuote = again.GetProperty("options")[0].GetProperty("quoteId").GetString();

        var responses = await Task.WhenAll(
            SendAsync(client, HttpMethod.Post, "/api/v1/rides", rider, new { quoteId = autoQuote }),
            SendAsync(client, HttpMethod.Post, "/api/v1/rides", rider, new { quoteId = secondQuote }));

        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        Assert.Equal("ride_rider_busy", await ErrorCodeAsync(responses.Single(r => !r.IsSuccessStatusCode)));
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Equal(1, await DbOf(scope).Rides.CountAsync(r => r.RiderUserId == Guid.Parse(riderId)));
    }

    // --- The rules only the database can enforce ---

    [DatabaseFact]
    public async Task The_database_itself_refuses_illegal_ride_changes()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        await TurnRidesAsync(client, admin, "available");
        var (driver, driverId) = await OnDutyDriverAsync(client, admin);
        var (rider, _) = await SignInAsync(client);
        var ride = await RequestRideAsync(client, rider);
        var id = Guid.Parse(ride.GetProperty("id").GetString()!);

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var sql = new NpgsqlConnection(DbOf(scope).Database.GetConnectionString());
        await sql.OpenAsync();

        async Task<PostgresException> RefusedAsync(string text)
        {
            await using var command = sql.CreateCommand();
            command.CommandText = text;
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("driver", Guid.Parse(driverId));
            return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        // A requested ride cannot skip to the end, be given a driver without being accepted, or keep a position.
        Assert.Equal("P0001", (await RefusedAsync("UPDATE rides SET status = 'completed' WHERE id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE rides SET driver_user_id = @driver WHERE id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE rides SET driver_latitude = 11.9, driver_longitude = 79.8, driver_location_at = now() WHERE id = @id")).SqlState);
        // The status trigger runs before the CHECK constraints, so an unknown status is refused by the trigger.
        Assert.Equal("P0001", (await RefusedAsync("UPDATE rides SET status = 'teleported' WHERE id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE rides SET pickup_latitude = 95 WHERE id = @id")).SqlState);

        // The fare is what was quoted; the rider and the quote cannot change; rides are kept.
        Assert.Equal("P0001", (await RefusedAsync("UPDATE rides SET fare = fare + 1 WHERE id = @id")).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("DELETE FROM rides WHERE id = @id")).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("TRUNCATE rides CASCADE")).SqlState);

        // Once taken it cannot go to a second driver, and a finished ride never changes again.
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/accept", driver));
        var other = await OnDutyDriverAsync(client, admin);
        var otherId = Guid.Parse(other.UserId);
        await using (var steal = sql.CreateCommand())
        {
            steal.CommandText = "UPDATE rides SET driver_user_id = @other WHERE id = @id";
            steal.Parameters.AddWithValue("other", otherId);
            steal.Parameters.AddWithValue("id", id);
            Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => steal.ExecuteNonQueryAsync())).SqlState);
        }

        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/rides/{id}/cancel", rider, new { reason = "test" }));
        Assert.Equal("P0001", (await RefusedAsync("UPDATE rides SET cancel_reason = 'edited' WHERE id = @id")).SqlState);
        Assert.Equal("P0001", (await RefusedAsync("UPDATE rides SET status = 'requested' WHERE id = @id")).SqlState);
    }

    [DatabaseFact]
    public async Task The_database_refuses_a_driver_on_two_active_rides_and_a_position_kept_off_duty()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        await TurnRidesAsync(client, admin, "available");
        var (driver, driverId) = await OnDutyDriverAsync(client, admin);
        var (riderA, _) = await SignInAsync(client);
        var (riderB, _) = await SignInAsync(client);
        var a = Guid.Parse((await RequestRideAsync(client, riderA)).GetProperty("id").GetString()!);
        var b = Guid.Parse((await RequestRideAsync(client, riderB)).GetProperty("id").GetString()!);
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{a}/accept", driver));

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var sql = new NpgsqlConnection(DbOf(scope).Database.GetConnectionString());
        await sql.OpenAsync();

        await using (var second = sql.CreateCommand())
        {
            second.CommandText = "UPDATE rides SET status = 'accepted', driver_user_id = @driver, accepted_at = now() WHERE id = @b";
            second.Parameters.AddWithValue("driver", Guid.Parse(driverId));
            second.Parameters.AddWithValue("b", b);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => second.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, refused.SqlState);
        }

        // The driver went off duty (the map is cleared): the database erased the position itself, and refuses to keep one for a driver off duty.
        await ClearTheMapAsync();
        await using (var count = sql.CreateCommand())
        {
            count.CommandText = "SELECT count(*) FROM ride_driver_positions WHERE user_id = @driver";
            count.Parameters.AddWithValue("driver", Guid.Parse(driverId));
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
        }

        await using var stale = sql.CreateCommand();
        stale.CommandText = "INSERT INTO ride_driver_positions (user_id, latitude, longitude, recorded_at) VALUES (@driver, 11.9, 79.8, now())";
        stale.Parameters.AddWithValue("driver", Guid.Parse(driverId));
        var kept = await Assert.ThrowsAsync<PostgresException>(() => stale.ExecuteNonQueryAsync());
        Assert.Equal("P0001", kept.SqlState);
        Assert.Contains("only while the driver is on duty", kept.MessageText);
    }

    [DatabaseFact]
    public async Task Reporting_positions_does_not_make_an_administrators_review_or_the_drivers_duty_switch_fail()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        var (driver, driverId) = await OnDutyDriverAsync(client, admin);

        // The administrator opens the driver, the driver's app keeps reporting where it is, and the administrator then suspends: this used to be
        // refused with "changed since you loaded it" because every position changed the profile's version.
        var opened = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/admin/ride-drivers/{driverId}", admin));
        for (var i = 0; i < 3; i++)
        {
            await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/ride-driver/location", driver, new { latitude = 11.93 + (i * 0.001), longitude = 79.83 }));
        }

        var suspended = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/ride-drivers/{driverId}/suspend", admin, new { version = VersionOf(opened), reason = "Safety complaint" });
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);

        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.False(await DbOf(scope).RideDriverPositions.AnyAsync(p => p.UserId == Guid.Parse(driverId)));
    }

    [DatabaseFact]
    public async Task A_driver_can_go_off_duty_after_their_app_has_been_reporting_positions()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        var (driver, driverId) = await OnDutyDriverAsync(client, admin);
        var loaded = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/ride-driver/me", driver));

        for (var i = 0; i < 3; i++)
        {
            await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/ride-driver/location", driver, new { latitude = 11.93, longitude = 79.83 + (i * 0.001) }));
        }

        var off = await SendAsync(client, HttpMethod.Post, "/api/v1/ride-driver/duty", driver, new { onDuty = false, version = VersionOf(loaded) });

        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.False(await DbOf(scope).RideDriverPositions.AnyAsync(p => p.UserId == Guid.Parse(driverId)));
    }

    [DatabaseFact]
    public async Task One_licence_and_one_vehicle_belong_to_one_ride_driver_account()
    {
        using var client = Factory.CreateClient();
        var plate = NewPlate();
        var licence = NewLicence();
        var (first, _) = await SignInAsync(client);
        var (second, _) = await SignInAsync(client);
        await ReadAsync(await SendAsync(client, HttpMethod.Put, "/api/v1/ride-driver/me", first, Application(plate, licence)));

        var sameLicence = await SendAsync(client, HttpMethod.Put, "/api/v1/ride-driver/me", second, Application(NewPlate(), licence));
        var sameVehicle = await SendAsync(client, HttpMethod.Put, "/api/v1/ride-driver/me", second, Application(plate, NewLicence()));

        Assert.Equal("ride_driver_document_in_use", await ErrorCodeAsync(sameLicence));
        Assert.Equal("ride_driver_document_in_use", await ErrorCodeAsync(sameVehicle));
    }

    // --- Safety (step 9.3) ---

    private async Task<(string Driver, string Rider, string RideId)> AcceptedRideAsync(HttpClient client, string admin)
    {
        await ClearTheMapAsync();
        await TurnRidesAsync(client, admin, "available");
        var (driver, _) = await OnDutyDriverAsync(client, admin);
        var (rider, _) = await SignInAsync(client, "Anita Raman");
        var ride = await RequestRideAsync(client, rider);
        var id = ride.GetProperty("id").GetString()!;
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/accept", driver));
        return (driver, rider, id);
    }

    [DatabaseFact]
    public async Task An_SOS_reaches_the_staff_queue_and_the_operations_page_and_staff_can_close_it()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (driver, rider, id) = await AcceptedRideAsync(client, admin);

        var receipt = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/rides/{id}/sos", rider, new { latitude = 11.95, longitude = 79.83 }));
        Assert.Equal("112", receipt.GetProperty("emergencyNumber").GetString());
        var again = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/rides/{id}/sos", rider, new { }));
        Assert.Equal(receipt.GetProperty("id").GetString(), again.GetProperty("id").GetString());
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/incidents", driver, new { category = "harassment", details = "Rider was abusive." }));

        var operations = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/operations", admin));
        Assert.Contains(operations.GetProperty("alerts").EnumerateArray(), a => a.GetProperty("code").GetString() == "ride_sos" && a.GetProperty("severity").GetString() == "critical");

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/ride-safety", admin));
        var first = list.GetProperty("items")[0];
        Assert.Equal("sos", first.GetProperty("kind").GetString());
        var reportId = first.GetProperty("id").GetString();
        var detail = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/admin/ride-safety/{reportId}", admin));
        Assert.Equal("Anita Raman", detail.GetProperty("rider").GetProperty("name").GetString());
        var closed = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/ride-safety/{reportId}/resolve", admin, new { version = VersionOf(detail), note = "Called both; safe." }));
        Assert.Equal("resolved", closed.GetProperty("status").GetString());
    }

    [DatabaseFact]
    public async Task Only_support_staff_see_the_safety_queue_and_nobody_else_can_report_on_a_ride()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, rider, id) = await AcceptedRideAsync(client, admin);
        var (stranger, _) = await SignInAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/ride-safety")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/ride-safety", rider)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/v1/rides/{id}/sos", stranger, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/v1/rides/{id}/incidents", stranger, new { category = "other", details = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/sos", rider, new { })).StatusCode);
    }

    [DatabaseFact]
    public async Task A_shared_trip_link_works_without_an_account_shows_little_and_stops_when_the_ride_ends()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (driver, rider, id) = await AcceptedRideAsync(client, admin);
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/ride-driver/rides/{id}/location", driver, new { latitude = 11.95, longitude = 79.83 }));
        var link = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/rides/{id}/share", rider));
        var token = link.GetProperty("token").GetString();

        var response = await client.GetAsync($"/api/v1/shared-rides/{token}");
        var shared = await ReadAsync(response);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("Ravi", shared.GetProperty("driverFirstName").GetString());
        Assert.Equal(11.95, shared.GetProperty("driverLocation").GetProperty("latitude").GetDouble());
        Assert.DoesNotContain("fare", shared.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", shared.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/shared-rides/not-a-link")).StatusCode);

        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/rides/{id}/cancel", rider));
        var ended = await ReadAsync(await client.GetAsync($"/api/v1/shared-rides/{token}"));
        Assert.True(ended.GetProperty("ended").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ended.GetProperty("driverFirstName").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, $"/api/v1/rides/{id}/share", rider)).StatusCode);
    }

    [DatabaseFact]
    public async Task The_database_keeps_safety_reports_and_the_hub_only_lets_signed_in_people_in()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (_, rider, id) = await AcceptedRideAsync(client, admin);
        var report = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/rides/{id}/incidents", rider, new { category = "overcharge", details = "Asked for more." }));
        var reportId = Guid.Parse(report.GetProperty("id").GetString()!);

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var sql = new NpgsqlConnection(DbOf(scope).Database.GetConnectionString());
        await sql.OpenAsync();
        foreach (var text in new[] { "DELETE FROM ride_safety_events WHERE id = @id", "TRUNCATE ride_safety_events CASCADE", "UPDATE ride_safety_events SET kind = 'sos' WHERE id = @id" })
        {
            await using var command = sql.CreateCommand();
            command.CommandText = text;
            command.Parameters.AddWithValue("id", reportId);
            var refused = await Assert.ThrowsAnyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains(refused.SqlState, new[] { "P0001", PostgresErrorCodes.CheckViolation });
        }

        // The hub's negotiate step needs a sign-in.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/hubs/rides/negotiate?negotiateVersion=1", null)).StatusCode);
        var signedIn = await SendAsync(client, HttpMethod.Post, "/hubs/rides/negotiate?negotiateVersion=1", rider);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [DatabaseFact]
    public async Task Deleting_an_account_removes_the_ride_driver_profile_and_is_blocked_while_on_duty()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        await ClearTheMapAsync();
        var (driver, driverId) = await OnDutyDriverAsync(client, admin);

        var blocked = await SendAsync(client, HttpMethod.Post, "/api/v1/account/deletion/code", driver);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("deletion_blocked", await ErrorCodeAsync(blocked));
        Assert.Contains("driver_on_duty", await blocked.Content.ReadAsStringAsync());

        var view = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/ride-driver/me", driver));
        await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/ride-driver/duty", driver, new { onDuty = false, version = VersionOf(view) }));
        var deleted = await DeleteAccountAsync(client, driver);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.False(await DbOf(scope).RideDriverProfiles.AnyAsync(p => p.UserId == Guid.Parse(driverId)));
    }
}
