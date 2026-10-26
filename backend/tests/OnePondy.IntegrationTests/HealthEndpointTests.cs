using System.Net;

namespace OnePondy.IntegrationTests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_is_healthy_without_a_database()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_is_unhealthy_when_the_database_is_unreachable()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [DatabaseFact]
    public async Task Readiness_is_healthy_when_the_database_is_reachable()
    {
        await using var factory = new ApiFactory(Environment.GetEnvironmentVariable(DatabaseFactAttribute.VariableName)!);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
