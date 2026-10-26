using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnePondy.Application.Identity;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.IntegrationTests;

/// <summary>
/// End-to-end account flows against a real, migrated PostgreSQL database. They run only when
/// ONEPONDY_TEST_DATABASE is set (see README).
/// </summary>
public sealed class AuthFlowTests : IAsyncLifetime
{
    private record OtpRequested(Guid ChallengeId, DateTimeOffset ExpiresAt, int ResendAfterSeconds, string MaskedDestination);

    private record SessionUser(Guid Id, string DisplayName, string[] Roles);

    private record Session(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt,
        SessionUser User,
        bool IsNewUser);

    private record Profile(Guid Id, string DisplayName, string? PhoneNumber, string? Email, string PreferredLanguage);

    private ApiFactory? _factory;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(DatabaseFactAttribute.VariableName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        // Codes can be re-requested at once here; the limits themselves are covered by unit tests.
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

    private ApiFactory Factory => _factory ?? throw new InvalidOperationException("No test database configured.");

    [DatabaseFact]
    public async Task Sign_in_profile_refresh_and_reuse_detection()
    {
        using var client = Factory.CreateClient();
        var phone = ApiFactory.NewIndianMobile();

        var session = await SignInAsync(client, phone);
        Assert.True(session.IsNewUser);
        Assert.Contains("customer", session.User.Roles);

        var profile = await GetProfileAsync(client, session.AccessToken);
        Assert.Equal("+91" + phone, profile.PhoneNumber);

        var renamed = await SendAsync(client, HttpMethod.Patch, "/api/v1/profile", session.AccessToken, new { displayName = "Asha" });
        Assert.Equal("Asha", (await renamed.Content.ReadFromJsonAsync<Profile>())!.DisplayName);

        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var rotated = (await refreshed.Content.ReadFromJsonAsync<Session>())!;
        Assert.NotEqual(session.RefreshToken, rotated.RefreshToken);

        var reused = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal("invalid_refresh_token", await AuthEndpointTests.ProblemCodeAsync(reused));

        // Reuse revoked the whole session, so even the newest access token is refused.
        var afterReuse = await SendAsync(client, HttpMethod.Get, "/api/v1/profile", rotated.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [DatabaseFact]
    public async Task Signing_out_revokes_access_immediately()
    {
        using var client = Factory.CreateClient();
        var session = await SignInAsync(client, ApiFactory.NewIndianMobile());

        var signedOut = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/logout", session.AccessToken);
        var profile = await SendAsync(client, HttpMethod.Get, "/api/v1/profile", session.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [DatabaseFact]
    public async Task Deleting_the_account_in_the_app_anonymises_it_and_ends_sessions()
    {
        using var client = Factory.CreateClient();
        var phone = ApiFactory.NewIndianMobile();
        var session = await SignInAsync(client, phone);

        var requested = await SendAsync(client, HttpMethod.Post, "/api/v1/account/deletion/code", session.AccessToken);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var challenge = (await requested.Content.ReadFromJsonAsync<OtpRequested>())!;

        var deleted = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/account/deletion/confirm",
            session.AccessToken,
            new { challengeId = challenge.ChallengeId, code = Factory.Codes.For("+91" + phone) });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/v1/profile", session.AccessToken)).StatusCode);

        // The number is free again: signing in creates a new, empty account.
        var again = await SignInAsync(client, phone);
        Assert.True(again.IsNewUser);
        Assert.NotEqual(session.User.Id, again.User.Id);
    }

    [DatabaseFact]
    public async Task The_public_deletion_page_flow_deletes_the_account()
    {
        using var client = Factory.CreateClient();
        var phone = ApiFactory.NewIndianMobile();
        var session = await SignInAsync(client, phone);

        var requested = await client.PostAsJsonAsync(
            "/api/v1/account/deletion-requests",
            new { channel = "sms", destination = phone });
        var challenge = (await requested.Content.ReadFromJsonAsync<OtpRequested>())!;
        var confirmed = await client.PostAsJsonAsync(
            "/api/v1/account/deletion-requests/confirm",
            new { challengeId = challenge.ChallengeId, code = Factory.Codes.For("+91" + phone) });

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/v1/profile", session.AccessToken)).StatusCode);
    }

    [DatabaseFact]
    public async Task Retention_purge_removes_expired_codes_and_ended_sessions()
    {
        using var client = Factory.CreateClient();
        var session = await SignInAsync(client, ApiFactory.NewIndianMobile());
        await SendAsync(client, HttpMethod.Post, "/api/v1/auth/logout", session.AccessToken);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();
        var later = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(100));
        var purged = await new AuthDataRetention(db, later).PurgeAsync(CancellationToken.None);

        Assert.True(purged.Challenges >= 1);
        Assert.True(purged.Sessions >= 1);
        Assert.False(await db.UserSessions.AnyAsync(s => s.UserId == session.User.Id));
    }

    private async Task<Session> SignInAsync(HttpClient client, string phone)
    {
        var requested = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = phone });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var challenge = (await requested.Content.ReadFromJsonAsync<OtpRequested>())!;

        var verified = await client.PostAsJsonAsync(
            "/api/v1/auth/otp/verify",
            new { challengeId = challenge.ChallengeId, code = Factory.Codes.For("+91" + phone), deviceName = "Integration test" });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        return (await verified.Content.ReadFromJsonAsync<Session>())!;
    }

    private static async Task<Profile> GetProfileAsync(HttpClient client, string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/api/v1/profile", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Profile>())!;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string accessToken,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }
}
