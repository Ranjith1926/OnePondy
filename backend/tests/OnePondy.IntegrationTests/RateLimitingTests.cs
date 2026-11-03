using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OnePondy.Api.Http;
using OnePondy.Application.Operations;

namespace OnePondy.IntegrationTests;

/// <summary>Which class of request each endpoint belongs to (spec Phase 10, step 10.4). No server is needed.</summary>
public class RateLimitClassifierTests
{
    [Theory]
    [InlineData("GET", "/api/v1/places", false, RateLimitClass.Browse)]
    [InlineData("GET", "/api/v1/restaurants/abc/menu", false, RateLimitClass.Browse)]
    [InlineData("GET", "/api/v1/config", false, RateLimitClass.Browse)]
    [InlineData("GET", "/api/v1/reviews", false, RateLimitClass.Browse)]
    [InlineData("GET", "/api/v1/support/grievance", false, RateLimitClass.Browse)]
    [InlineData("HEAD", "/api/v1/places", false, RateLimitClass.Browse)]
    [InlineData("GET", "/api/v1/bookings", true, RateLimitClass.Read)]
    [InlineData("GET", "/api/v1/partner/businesses", true, RateLimitClass.Read)]
    [InlineData("PUT", "/api/v1/saved-places/abc", true, RateLimitClass.Write)]
    [InlineData("POST", "/api/v1/bookings/abc/cancel", true, RateLimitClass.Write)]
    [InlineData("DELETE", "/api/v1/reviews/abc", true, RateLimitClass.Write)]
    [InlineData("POST", "/api/v1/reviews", true, RateLimitClass.Create)]
    [InlineData("POST", "/api/v1/reviews/", true, RateLimitClass.Create)]
    [InlineData("POST", "/api/v1/support/tickets", true, RateLimitClass.Create)]
    [InlineData("POST", "/api/v1/support/tickets/abc/messages", true, RateLimitClass.Create)]
    [InlineData("POST", "/api/v1/bookings", true, RateLimitClass.Create)]
    [InlineData("POST", "/api/v1/food/orders", true, RateLimitClass.Create)]
    [InlineData("POST", "/api/v1/trip-planner/plan", true, RateLimitClass.Create)]
    [InlineData("POST", "/api/v1/partner/businesses", true, RateLimitClass.Create)]
    [InlineData("POST", "/api/v1/admin/places/abc/media", true, RateLimitClass.Upload)]
    [InlineData("POST", "/api/v1/partner/businesses/abc/documents", true, RateLimitClass.Upload)]
    [InlineData("GET", "/api/v1/admin/places", true, RateLimitClass.Staff)]
    [InlineData("POST", "/api/v1/admin/reviews/abc/publish", true, RateLimitClass.Staff)]
    [InlineData("PUT", "/api/v1/admin/config/modules/food", true, RateLimitClass.Staff)]
    public void Each_request_belongs_to_the_class_that_fits_what_it_costs(string method, string path, bool signedIn, RateLimitClass expected) =>
        Assert.Equal(expected, RateLimitClassifier.Classify(method, new PathString(path), signedIn));

    [Theory]
    [InlineData("GET", "/health/live")]
    [InlineData("GET", "/health/ready")]
    [InlineData("GET", "/media/places/photo.jpg")]
    [InlineData("GET", "/openapi/v1.json")]
    [InlineData("POST", "/api/v1/webhooks/payments")]
    [InlineData("POST", "/api/v1/auth/otp/request")]
    [InlineData("POST", "/api/v1/auth/refresh")]
    [InlineData("DELETE", "/api/v1/account")]
    [InlineData("POST", "/api/v1/admin/auth/otp/verify")]
    public void Health_checks_files_gateway_callbacks_and_sign_in_are_not_in_these_classes(string method, string path) =>
        Assert.Null(RateLimitClassifier.Classify(method, new PathString(path), false));

    [Theory]
    [InlineData("/api/v1/auth/otp/request", true)]
    [InlineData("/api/v1/auth", true)]
    [InlineData("/api/v1/account/delete", true)]
    [InlineData("/api/v1/admin/auth/otp/request", true)]
    [InlineData("/api/v1/authors", false)]
    [InlineData("/api/v1/admin/authority", false)]
    [InlineData("/api/v1/places", false)]
    public void Sign_in_and_account_endpoints_are_recognised_by_their_whole_first_segment(string path, bool isAuth) =>
        Assert.Equal(isAuth, RateLimitClassifier.IsAuth(new PathString(path)));

    [Fact]
    public void A_path_that_only_starts_like_the_admin_area_is_not_staff()
    {
        Assert.Equal(RateLimitClass.Browse, RateLimitClassifier.Classify("GET", new PathString("/api/v1/administration"), false));
    }
}

/// <summary>The limits themselves, over HTTP and without a database: they refuse, say when to come back, recover, and leave the exempt alone.</summary>
public class RateLimitingBehaviourTests
{
    private static ApiFactory Factory(params (string Key, string Value)[] settings) =>
        new(ApiFactory.UnreachableDatabase, settings.ToDictionary(s => s.Key, s => s.Value));

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task A_public_read_is_refused_after_the_limit_with_when_to_come_back_and_works_again_after_the_window()
    {
        await using var factory = Factory(("RateLimiting:Browse:PermitLimit", "3"), ("RateLimiting:Browse:Window", "00:00:02"));
        using var client = factory.CreateClient();

        for (var request = 0; request < 3; request++)
        {
            // Not signed in, so the API answers 401; what matters here is that it answers at all.
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
        }

        var limited = await client.GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", await CodeAsync(limited));
        Assert.NotNull(limited.Headers.RetryAfter);

        await Task.Delay(TimeSpan.FromSeconds(2.5));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
    }

    [Fact]
    public async Task Creating_things_has_a_tighter_limit_than_other_changes()
    {
        await using var factory = Factory(("RateLimiting:Create:PermitLimit", "2"), ("RateLimiting:Write:PermitLimit", "1000"));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/reviews", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/support/tickets", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/bookings", new { })).StatusCode);

        // A different kind of change is counted on its own.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/v1/bookings/{Guid.NewGuid()}/cancel", new { })).StatusCode);
    }

    [Fact]
    public async Task Health_checks_gateway_callbacks_and_sign_in_are_never_refused_by_these_limits()
    {
        await using var factory = Factory(
            ("RateLimiting:Browse:PermitLimit", "1"),
            ("RateLimiting:Write:PermitLimit", "1"),
            ("RateLimiting:Create:PermitLimit", "1"));
        using var client = factory.CreateClient();

        for (var request = 0; request < 6; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/webhooks/payments", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = "12345" })).StatusCode);
        }
    }

    [Fact]
    public async Task Sign_in_keeps_its_own_limit_and_a_limited_class_does_not_use_it_up()
    {
        await using var factory = Factory(("RateLimiting:Auth:PermitLimit", "2"), ("RateLimiting:Browse:PermitLimit", "1000"));
        using var client = factory.CreateClient();
        var invalid = new { channel = "sms", destination = "12345" };

        for (var request = 0; request < 5; request++)
        {
            await client.GetAsync("/api/v1/profile");
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/otp/request", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/otp/request", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/auth/otp/request", invalid)).StatusCode);
    }

    [Fact]
    public async Task The_limiter_counts_what_it_does_by_class_and_keeps_no_addresses_or_accounts()
    {
        await using var factory = Factory(("RateLimiting:Browse:PermitLimit", "2"));
        using var client = factory.CreateClient();
        for (var request = 0; request < 4; request++)
        {
            await client.GetAsync("/api/v1/profile");
        }

        await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = "12345" });
        var snapshot = factory.Services.GetRequiredService<IRateLimitStatistics>().Snapshot();

        var browse = snapshot.Classes.Single(c => c.Name == "browse");
        Assert.Equal(4, browse.Requests);
        Assert.Equal(2, browse.Rejected);
        Assert.NotNull(browse.LastRejectedAt);
        var auth = snapshot.Classes.Single(c => c.Name == "auth");
        Assert.Equal(1, auth.Requests);
        Assert.Equal(0, auth.Rejected);
        Assert.Null(auth.LastRejectedAt);
        Assert.DoesNotContain(typeof(RateLimitClassStatistics).GetProperties(), p => p.Name.Contains("Address") || p.Name.Contains("User") || p.Name.Contains("Ip"));
    }

    [Fact]
    public async Task A_signed_in_request_with_a_bad_token_is_counted_with_the_address_not_as_someone()
    {
        await using var factory = Factory(("RateLimiting:Browse:PermitLimit", "2"), ("RateLimiting:Read:PermitLimit", "1000"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        await client.GetAsync("/api/v1/profile");
        await client.GetAsync("/api/v1/profile");
        var third = await client.GetAsync("/api/v1/profile");

        // A forged token does not make a request a signed-in one: it is limited as the public read it is.
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}

/// <summary>The limits count each signed-in person on their own, which needs real accounts.</summary>
public sealed class RateLimitingPerPersonTests : IAsyncLifetime
{
    private ApiFactory? _factory;

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
            ["RateLimiting:Create:PermitLimit"] = "2",
            ["RateLimiting:Create:Window"] = "00:00:03",
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

    private async Task<HttpClient> SignedInAsync()
    {
        var client = _factory!.CreateClient();
        var phone = ApiFactory.NewIndianMobile();
        var requested = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { channel = "sms", destination = phone });
        var challengeId = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetString();
        var verified = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId, code = _factory.Codes.For("+91" + phone) });
        var token = (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [DatabaseFact]
    public async Task One_persons_limit_does_not_use_up_anothers_even_from_the_same_address()
    {
        using var first = await SignedInAsync();
        using var second = await SignedInAsync();
        var body = new { topic = "other", subject = "x", message = "y" };

        // Both come from the same test address; the refusals still fall on the person who made the requests.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await first.PostAsJsonAsync("/api/v1/support/tickets", body)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await first.PostAsJsonAsync("/api/v1/support/tickets", body)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsJsonAsync("/api/v1/support/tickets", body)).StatusCode);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await second.PostAsJsonAsync("/api/v1/support/tickets", body)).StatusCode);

        // And the first person can start again once the window has passed.
        await Task.Delay(TimeSpan.FromSeconds(3.5));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await first.PostAsJsonAsync("/api/v1/support/tickets", body)).StatusCode);
    }
}
