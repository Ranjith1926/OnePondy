using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Partner onboarding (spec §10, Phase 6): applying, document upload, review and the admin workflow,
/// against a real, migrated PostgreSQL database. Runs only when ONEPONDY_TEST_DATABASE is set.
/// </summary>
public sealed class BusinessApiTests : IAsyncLifetime
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

    private static object ApplyBody(
        string name,
        string businessType = "activity",
        string? fssaiLicenseNumber = null) => new
        {
            name,
            businessType,
            description = "A short description of the business.",
            phoneNumber = "9876543210",
            email = "owner@example.com",
            address = "Puducherry",
            lat = 11.9,
            lng = 79.83,
            gstin = (string?)null,
            fssaiLicenseNumber,
        };

    /// <summary>A valid administrator session, created directly (skipping the OTP ceremony and the
    /// bootstrap-administrator race, which other test files already cover).</summary>
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

    [DatabaseFact]
    public async Task A_restaurant_cannot_be_approved_without_an_fssai_licence()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var owner = await SignInCustomerAsync(client);

        var applied = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/partner/businesses", owner, ApplyBody("Pondy Diner", "restaurant")));
        var id = applied.GetProperty("id").GetString();
        var version = applied.GetProperty("version").GetUInt32();

        var rejectedApproval = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{id}/approve", admin, new { version });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedApproval.StatusCode);

        var updated = await ReadAsync(await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/partner/businesses/{id}",
            owner,
            new { version, business = ApplyBody("Pondy Diner", "restaurant", "11223344556677") }));
        version = updated.GetProperty("version").GetUInt32();

        var approved = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{id}/approve", admin, new { version }));
        Assert.Equal("approved", approved.GetProperty("approvalStatus").GetString());
        Assert.True(approved.GetProperty("isActive").GetBoolean());
    }

    [DatabaseFact]
    public async Task A_partner_can_only_see_and_manage_their_own_business()
    {
        using var client = Factory.CreateClient();
        var owner = await SignInCustomerAsync(client);
        var stranger = await SignInCustomerAsync(client);

        var applied = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/partner/businesses", owner, ApplyBody("Lagoon Kayaks")));
        var id = applied.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{id}", stranger)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/admin/businesses", owner)).StatusCode);

        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3, 4]), "file", "gstin.jpg" },
            { new StringContent("gstin"), "kind" },
        };
        var uploadRequest = Authorized(HttpMethod.Post, $"/api/v1/partner/businesses/{id}/documents", owner);
        uploadRequest.Content = content;
        var uploaded = await ReadAsync(await client.SendAsync(uploadRequest));
        var documentId = uploaded.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{id}/documents/{documentId}", stranger)).StatusCode);
        var downloaded = await SendAsync(client, HttpMethod.Get, $"/api/v1/partner/businesses/{id}/documents/{documentId}", owner);
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await downloaded.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Delete, $"/api/v1/partner/businesses/{id}/documents/{documentId}", stranger)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/api/v1/partner/businesses/{id}/documents/{documentId}", owner)).StatusCode);
    }

    [DatabaseFact]
    public async Task Rejecting_an_application_lets_the_owner_fix_it_and_resubmit()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var owner = await SignInCustomerAsync(client);

        var applied = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/partner/businesses", owner, ApplyBody("Hilltop Shop", "shop")));
        var id = applied.GetProperty("id").GetString();
        var version = applied.GetProperty("version").GetUInt32();

        var rejected = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, $"/api/v1/admin/businesses/{id}/reject", admin, new { version, reason = "The address looks incomplete." }));
        Assert.Equal("rejected", rejected.GetProperty("approvalStatus").GetString());
        Assert.Equal("The address looks incomplete.", rejected.GetProperty("reviewNote").GetString());
        version = rejected.GetProperty("version").GetUInt32();

        var missingReason = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{id}/reject", admin, new { version, reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        var resubmitted = await ReadAsync(await SendAsync(
            client, HttpMethod.Put, $"/api/v1/partner/businesses/{id}", owner, new { version, business = ApplyBody("Hilltop Shop", "shop") }));
        Assert.Equal("pending", resubmitted.GetProperty("approvalStatus").GetString());
        Assert.Null(resubmitted.GetProperty("reviewNote").GetString());
    }

    [DatabaseFact]
    public async Task An_approved_business_can_be_suspended_and_reinstated_but_not_edited()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var owner = await SignInCustomerAsync(client);

        var applied = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/partner/businesses", owner, ApplyBody("River View Stay", "hotel")));
        var id = applied.GetProperty("id").GetString();
        var version = applied.GetProperty("version").GetUInt32();

        var approved = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{id}/approve", admin, new { version }));
        version = approved.GetProperty("version").GetUInt32();

        var editAttempt = await SendAsync(client, HttpMethod.Put, $"/api/v1/partner/businesses/{id}", owner, new { version, business = ApplyBody("River View Stay", "hotel") });
        Assert.Equal(HttpStatusCode.Conflict, editAttempt.StatusCode);

        var suspended = await ReadAsync(await SendAsync(
            client, HttpMethod.Post, $"/api/v1/admin/businesses/{id}/suspend", admin, new { version, reason = "Guests reported it was closed." }));
        Assert.Equal("suspended", suspended.GetProperty("approvalStatus").GetString());
        Assert.False(suspended.GetProperty("isActive").GetBoolean());
        version = suspended.GetProperty("version").GetUInt32();

        var reinstated = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{id}/reinstate", admin, new { version }));
        Assert.Equal("approved", reinstated.GetProperty("approvalStatus").GetString());
        Assert.True(reinstated.GetProperty("isActive").GetBoolean());
    }

    [DatabaseFact]
    public async Task Acting_on_a_stale_version_is_rejected()
    {
        using var client = Factory.CreateClient();
        var admin = await AdministratorTokenAsync();
        var owner = await SignInCustomerAsync(client);

        var applied = await ReadAsync(await SendAsync(client, HttpMethod.Post, "/api/v1/partner/businesses", owner, ApplyBody("Backwater Tours", "tourOperator")));
        var id = applied.GetProperty("id").GetString();
        var staleVersion = applied.GetProperty("version").GetUInt32();

        await SendAsync(client, HttpMethod.Put, $"/api/v1/partner/businesses/{id}", owner, new { version = staleVersion, business = ApplyBody("Backwater Tours Updated", "tourOperator") });

        var staleApproval = await SendAsync(client, HttpMethod.Post, $"/api/v1/admin/businesses/{id}/approve", admin, new { version = staleVersion });
        Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);
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

    private static async Task<System.Text.Json.JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} -> {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    }
}
