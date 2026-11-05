using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OnePondy.Api.Http;

namespace OnePondy.IntegrationTests;

/// <summary>The API reference page and description: who can see them, and that they describe the API honestly.</summary>
public class ApiDocsTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "OnePondy.Api";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Settings(string? enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(enabled is null ? [] : new Dictionary<string, string?> { [ApiDocs.SettingName] = enabled })
            .Build();

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Development", "false", true)]
    [InlineData("Testing", null, false)]
    [InlineData("Testing", "false", false)]
    [InlineData("Testing", "true", true)]
    [InlineData("Staging", "true", true)]
    [InlineData("Production", null, false)]
    [InlineData("Production", "true", false)]
    public void The_reference_is_on_in_development_on_a_test_site_only_when_asked_and_never_in_production(
        string environment, string? setting, bool expected) =>
        Assert.Equal(expected, ApiDocs.IsEnabled(new FakeEnvironment(environment), Settings(setting)));

    [Fact]
    public async Task A_test_site_serves_neither_page_unless_the_setting_is_on()
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Fact]
    public async Task With_the_setting_on_the_page_and_the_description_are_served_and_only_signed_in_endpoints_carry_a_lock()
    {
        await using var factory = new ApiFactory(
            ApiFactory.UnreachableDatabase,
            new Dictionary<string, string> { [ApiDocs.SettingName] = "true" });
        using var client = factory.CreateClient();

        var page = await client.GetAsync("/scalar");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("OnePondy API", document.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("bearer", document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());

        bool Locked(string path, string method) =>
            document.GetProperty("paths").GetProperty(path).GetProperty(method).TryGetProperty("security", out var security)
            && security.GetArrayLength() > 0;

        Assert.False(Locked("/api/v1/listings", "get"));
        Assert.False(Locked("/api/v1/auth/otp/request", "post"));
        Assert.True(Locked("/api/v1/profile", "get"));
        Assert.True(Locked("/api/v1/bookings", "post"));
    }
}
