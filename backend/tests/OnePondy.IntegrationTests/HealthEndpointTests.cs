using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OnePondy.IntegrationTests;

public class HealthEndpointTests
{
    // Nothing listens on port 1, so connections are refused immediately.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=2";

    [Fact]
    public async Task Liveness_is_healthy_without_a_database()
    {
        await using var factory = CreateFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_is_unhealthy_when_the_database_is_unreachable()
    {
        await using var factory = CreateFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [DatabaseFact]
    public async Task Readiness_is_healthy_when_the_database_is_reachable()
    {
        await using var factory = CreateFactory(Environment.GetEnvironmentVariable(DatabaseFactAttribute.VariableName)!);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:OnePondy", connectionString));
}
