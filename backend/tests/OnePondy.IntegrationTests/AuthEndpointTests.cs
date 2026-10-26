using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OnePondy.IntegrationTests;

/// <summary>HTTP behaviour that does not need a database: limits, authentication and the public page.</summary>
public class AuthEndpointTests
{
    [Fact]
    public async Task Auth_endpoints_are_rate_limited_per_client()
    {
        await using var factory = new ApiFactory(
            ApiFactory.UnreachableDatabase,
            new Dictionary<string, string> { ["RateLimiting:Auth:PermitLimit"] = "3" });
        using var client = factory.CreateClient();
        var invalid = new { channel = "sms", destination = "12345" };

        for (var request = 0; request < 3; request++)
        {
            var allowed = await client.PostAsJsonAsync("/api/v1/auth/otp/request", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
            Assert.Equal("invalid_destination", await ProblemCodeAsync(allowed));
        }

        var limited = await client.PostAsJsonAsync("/api/v1/auth/otp/request", invalid);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", await ProblemCodeAsync(limited));
        Assert.True(limited.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task Profile_requires_a_valid_access_token()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        var anonymous = await client.GetAsync("/api/v1/profile");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        var forged = await client.GetAsync("/api/v1/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
    }

    [Fact]
    public async Task Rejects_SMS_numbers_outside_India()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/otp/request",
            new { channel = "sms", destination = "+14155550100" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unsupported_country", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Serves_the_public_account_deletion_page()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/account/delete/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Delete your OnePondy account", await response.Content.ReadAsStringAsync());
    }

    internal static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
