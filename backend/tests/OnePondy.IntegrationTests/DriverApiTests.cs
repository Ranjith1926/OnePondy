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
/// Delivery drivers (spec Phase 8, step 8.6), against a real, migrated PostgreSQL database: applying, review by an administrator,
/// shifts, access control, and the rules only the database can enforce. Runs only when ONEPONDY_TEST_DATABASE is set (see README).
/// </summary>
public sealed class DriverApiTests : IAsyncLifetime
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

    private async Task<(string Token, string Phone, string UserId)> SignInAsync(HttpClient client)
    {
        var phone = ApiFactory.NewIndianMobile();
        var requested = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = phone });
        var challengeId = (await ReadAsync(requested)).GetProperty("challengeId").GetString();
        var verified = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId, code = Factory.Codes.For("+91" + phone) });
        var token = (await ReadAsync(verified)).GetProperty("accessToken").GetString()!;
        var profile = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/profile", token));
        return (token, phone, profile.GetProperty("id").GetString()!);
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

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string NewLicence() => "PY" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();

    private static object Scooter(string licence, uint? version = null, string name = "Ravi Kumar") =>
        new { fullName = name, vehicleType = "scooter", vehicleNumber = "PY 01 AB 1234", licenceNumber = licence, version };

    private async Task<JsonElement> ApplyAsync(HttpClient client, string token, string? licence = null) =>
        await ReadAsync(await SendAsync(client, HttpMethod.Put, "/api/v1/driver/me", token, Scooter(licence ?? NewLicence())));

    private static uint VersionOf(JsonElement driver) => driver.GetProperty("version").GetUInt32();

    private async Task<(string Token, string Phone, string UserId, JsonElement Driver)> ApprovedDriverAsync(HttpClient client, string admin)
    {
        var (token, phone, userId) = await SignInAsync(client);
        var applied = await ApplyAsync(client, token);
        var approved = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/approve", admin, new { version = VersionOf(applied) });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var driver = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", token));
        return (token, phone, userId, driver);
    }

    // --- Tests ---

    [DatabaseFact]
    public async Task A_driver_applies_is_approved_and_works_a_shift()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (token, _, userId) = await SignInAsync(client);

        var before = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", token));
        Assert.False(before.GetProperty("configured").GetBoolean());

        var applied = await ApplyAsync(client, token);
        Assert.Equal("pending", applied.GetProperty("approvalStatus").GetString());
        Assert.Equal("PY01AB1234", applied.GetProperty("vehicleNumber").GetString());
        Assert.False(applied.GetProperty("canGoOnDuty").GetBoolean());

        // Not approved yet, so no shift.
        var early = await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = true, version = VersionOf(applied) });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("driver_not_approved", await ErrorCodeAsync(early));

        var detail = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/admin/drivers/{userId}", admin));
        Assert.StartsWith("+91", detail.GetProperty("phoneNumber").GetString());
        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/approve", admin, new { version = VersionOf(applied) }));

        var approved = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", token));
        Assert.Equal("approved", approved.GetProperty("approvalStatus").GetString());
        Assert.True(approved.GetProperty("canGoOnDuty").GetBoolean());

        var on = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = true, version = VersionOf(approved) }));
        Assert.True(on.GetProperty("isOnDuty").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, on.GetProperty("onDutySince").ValueKind);

        var off = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = false, version = VersionOf(on) }));
        Assert.False(off.GetProperty("isOnDuty").GetBoolean());
        Assert.Equal(JsonValueKind.Null, off.GetProperty("onDutySince").ValueKind);
    }

    [DatabaseFact]
    public async Task A_rejected_application_can_be_corrected_and_sent_again()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (token, _, userId) = await SignInAsync(client);
        var applied = await ApplyAsync(client, token);

        var needsReason = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/reject", admin, new { version = VersionOf(applied), reason = " " });
        Assert.Equal(HttpStatusCode.BadRequest, needsReason.StatusCode);

        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/reject", admin, new { version = VersionOf(applied), reason = "Licence number does not match" }));
        var rejected = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", token));
        Assert.Equal("rejected", rejected.GetProperty("approvalStatus").GetString());
        Assert.Equal("Licence number does not match", rejected.GetProperty("reviewNote").GetString());

        var again = await ReadAsync(await SendAsync(client, HttpMethod.Put, "/api/v1/driver/me", token, Scooter(NewLicence(), VersionOf(rejected))));
        Assert.Equal("pending", again.GetProperty("approvalStatus").GetString());
        Assert.Equal(JsonValueKind.Null, again.GetProperty("reviewNote").ValueKind);
    }

    [DatabaseFact]
    public async Task Suspending_takes_a_driver_off_duty_and_locks_their_profile_until_reinstated()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (token, _, userId, driver) = await ApprovedDriverAsync(client, admin);
        var on = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = true, version = VersionOf(driver) }));

        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/suspend", admin, new { version = VersionOf(on), reason = "Complaint under review" }));

        var suspended = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", token));
        Assert.Equal("suspended", suspended.GetProperty("approvalStatus").GetString());
        Assert.False(suspended.GetProperty("isOnDuty").GetBoolean());

        var edit = await SendAsync(client, HttpMethod.Put, "/api/v1/driver/me", token, Scooter(NewLicence(), VersionOf(suspended)));
        Assert.Equal("driver_locked", await ErrorCodeAsync(edit));
        var duty = await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = true, version = VersionOf(suspended) });
        Assert.Equal("driver_not_approved", await ErrorCodeAsync(duty));

        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/reinstate", admin, new { version = VersionOf(suspended) }));
        var back = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", token));
        Assert.Equal("approved", back.GetProperty("approvalStatus").GetString());
        Assert.False(back.GetProperty("isOnDuty").GetBoolean());
    }

    [DatabaseFact]
    public async Task Review_is_for_administrators_only()
    {
        using var client = Factory.CreateClient();
        var (token, _, userId) = await SignInAsync(client);
        var applied = await ApplyAsync(client, token);
        var other = (await SignInAsync(client)).Token;

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/drivers", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/drivers", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/approve", token, new { version = VersionOf(applied) })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, $"/api/v1/admin/drivers/{userId}", other)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", null)).StatusCode);

        // And nobody can touch someone else's profile: the routes only ever act on the caller.
        var theirs = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", other));
        Assert.False(theirs.GetProperty("configured").GetBoolean());
    }

    [DatabaseFact]
    public async Task The_review_list_filters_by_status()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (token, _, userId) = await SignInAsync(client);
        await ApplyAsync(client, token);

        var pending = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/drivers?status=pending&pageSize=100", admin));
        Assert.Contains(pending.GetProperty("items").EnumerateArray(), d => d.GetProperty("userId").GetString() == userId);

        var suspended = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/admin/drivers?status=suspended&pageSize=100", admin));
        Assert.DoesNotContain(suspended.GetProperty("items").EnumerateArray(), d => d.GetProperty("userId").GetString() == userId);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/drivers?status=nonsense", admin)).StatusCode);
    }

    [DatabaseFact]
    public async Task One_licence_belongs_to_one_account_even_when_two_apply_at_once()
    {
        using var client = Factory.CreateClient();
        var first = (await SignInAsync(client)).Token;
        var second = (await SignInAsync(client)).Token;
        var licence = NewLicence();

        await ApplyAsync(client, first, licence);
        var duplicate = await SendAsync(client, HttpMethod.Put, "/api/v1/driver/me", second, Scooter(licence.ToLowerInvariant()));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("driver_licence_in_use", await ErrorCodeAsync(duplicate));

        // Two new applications racing for one licence: exactly one wins, and the other is told, not given a server error.
        var third = (await SignInAsync(client)).Token;
        var fourth = (await SignInAsync(client)).Token;
        var raced = NewLicence();
        var results = await Task.WhenAll(
            SendAsync(client, HttpMethod.Put, "/api/v1/driver/me", third, Scooter(raced)),
            SendAsync(client, HttpMethod.Put, "/api/v1/driver/me", fourth, Scooter(raced)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [DatabaseFact]
    public async Task A_stale_edit_or_shift_change_is_refused_not_applied()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (token, _, userId, driver) = await ApprovedDriverAsync(client, admin);
        var oldVersion = VersionOf(driver);

        var on = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = true, version = oldVersion }));
        Assert.NotEqual(oldVersion, VersionOf(on));

        var staleEdit = await SendAsync(client, HttpMethod.Put, "/api/v1/driver/me", token, Scooter(NewLicence(), oldVersion));
        Assert.Equal(HttpStatusCode.Conflict, staleEdit.StatusCode);
        Assert.Equal("driver_changed", await ErrorCodeAsync(staleEdit));

        var staleReview = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/suspend", admin, new { version = oldVersion, reason = "x" });
        Assert.Equal("driver_changed", await ErrorCodeAsync(staleReview));

        var still = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/driver/me", token));
        Assert.True(still.GetProperty("isOnDuty").GetBoolean());
        Assert.Equal("approved", still.GetProperty("approvalStatus").GetString());
    }

    [DatabaseFact]
    public async Task Changing_the_vehicle_sends_an_approved_driver_back_for_review()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (token, _, _, driver) = await ApprovedDriverAsync(client, admin);
        var on = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = true, version = VersionOf(driver) }));

        var changed = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Put,
            "/api/v1/driver/me",
            token,
            new { fullName = "Ravi Kumar", vehicleType = "bicycle", version = VersionOf(on) }));

        Assert.Equal("pending", changed.GetProperty("approvalStatus").GetString());
        Assert.False(changed.GetProperty("isOnDuty").GetBoolean());
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("vehicleNumber").ValueKind);
    }

    [DatabaseFact]
    public async Task A_driver_on_duty_must_go_off_duty_before_deleting_their_account()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (token, phone, userId, driver) = await ApprovedDriverAsync(client, admin);
        var on = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = true, version = VersionOf(driver) }));

        var blocked = await SendAsync(client, HttpMethod.Post, "/api/v1/account/deletion/code", token);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("driver_on_duty", problem.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));

        await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/driver/duty", token, new { onDuty = false, version = VersionOf(on) }));
        var requested = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/account/deletion/code", token));
        var confirmed = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/account/deletion/confirm",
            token,
            new { challengeId = requested.GetProperty("challengeId").GetString(), code = Factory.Codes.For("+91" + phone) });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();
        Assert.False(await db.DriverProfiles.AnyAsync(p => p.UserId == Guid.Parse(userId)));
    }

    [DatabaseFact]
    public async Task The_database_keeps_driver_profiles_honest()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var (token, _, userId) = await SignInAsync(client);
        var applied = await ApplyAsync(client, token);
        var id = Guid.Parse(userId);

        await using var scope = Factory.Services.CreateAsyncScope();
        await using var sql = new NpgsqlConnection(scope.ServiceProvider.GetRequiredService<OnePondyDbContext>().Database.GetConnectionString());
        await sql.OpenAsync();

        async Task<PostgresException> RefusedAsync(string text)
        {
            await using var command = sql.CreateCommand();
            command.CommandText = text;
            command.Parameters.AddWithValue("id", id);
            return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        // Only an approved driver is ever on duty; "since" exists exactly while on duty.
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE driver_profiles SET is_on_duty = true, on_duty_since = now() WHERE user_id = @id")).SqlState);

        await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/drivers/{userId}/approve", admin, new { version = VersionOf(applied) }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE driver_profiles SET is_on_duty = true WHERE user_id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE driver_profiles SET on_duty_since = now() WHERE user_id = @id")).SqlState);

        // A rejection or suspension always says why, a motorised vehicle always has its papers, a bicycle never does.
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE driver_profiles SET approval_status = 'rejected', review_note = NULL WHERE user_id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE driver_profiles SET licence_number = NULL WHERE user_id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE driver_profiles SET vehicle_type = 'bicycle' WHERE user_id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE driver_profiles SET vehicle_type = 'helicopter' WHERE user_id = @id")).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await RefusedAsync("UPDATE driver_profiles SET approval_status = 'banned' WHERE user_id = @id")).SqlState);

        // The profile belongs to a real account.
        await using var orphan = sql.CreateCommand();
        orphan.CommandText =
            "INSERT INTO driver_profiles (user_id, full_name, vehicle_type, approval_status, is_on_duty, created_at, updated_at) " +
            "VALUES (gen_random_uuid(), 'Nobody', 'bicycle', 'pending', false, now(), now())";
        var missingAccount = await Assert.ThrowsAsync<PostgresException>(() => orphan.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, missingAccount.SqlState);
    }
}
