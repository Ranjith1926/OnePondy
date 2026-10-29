using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OnePondy.Application.Identity;
using OnePondy.Infrastructure;
using OnePondy.Infrastructure.Messaging;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.UnitTests.Infrastructure;

public class DependencyInjectionTests
{
    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "OnePondy.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

    private const string ConnectionString = "Host=localhost;Database=onepondy_test;Username=test";

    [Fact]
    public void AddInfrastructure_without_connection_string_fails_with_setup_guidance()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(Configuration(), new TestEnvironment("Development")));

        Assert.Contains("ConnectionStrings:OnePondy", exception.Message);
        Assert.Contains("user-secrets", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_registers_DbContext_with_the_PostgreSQL_provider()
    {
        var configuration = Configuration(("ConnectionStrings:OnePondy", ConnectionString));

        using var provider = new ServiceCollection()
            .AddInfrastructure(configuration, new TestEnvironment("Development"))
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    [Fact]
    public void Codes_are_not_sent_anywhere_until_a_provider_is_configured()
    {
        var configuration = Configuration(("ConnectionStrings:OnePondy", ConnectionString));

        using var provider = new ServiceCollection()
            .AddInfrastructure(configuration, new TestEnvironment("Production"))
            .BuildServiceProvider();

        Assert.IsType<UnavailableOtpSender>(provider.GetRequiredService<IOtpSender>());
    }

    [Fact]
    public void Logging_codes_is_refused_outside_development()
    {
        var configuration = Configuration(
            ("ConnectionStrings:OnePondy", ConnectionString),
            ("Auth:Otp:Delivery", "Log"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(configuration, new TestEnvironment("Production")));

        Assert.Contains("only allowed in Development", exception.Message);
    }

    [Fact]
    public void Twilio_delivery_without_credentials_fails_at_startup_with_guidance()
    {
        var configuration = Configuration(
            ("ConnectionStrings:OnePondy", ConnectionString),
            ("Auth:Otp:Delivery", "Twilio"),
            ("Twilio:AccountSid", "ACtest"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(configuration, new TestEnvironment("Production")));

        Assert.Contains("Twilio:AuthToken", exception.Message);
    }

    [Fact]
    public void Twilio_delivery_with_credentials_registers_the_twilio_sender()
    {
        var configuration = Configuration(
            ("ConnectionStrings:OnePondy", ConnectionString),
            ("Auth:Jwt:SigningKey", Convert.ToBase64String(new byte[32])),
            ("Auth:HashingKey", Convert.ToBase64String(new byte[32])),
            ("Auth:Otp:Delivery", "Twilio"),
            ("Twilio:AccountSid", "ACtest"),
            ("Twilio:AuthToken", "token"),
            ("Twilio:From", "+15005550006"));
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration, new TestEnvironment("Production"));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<TwilioOtpSender>(provider.GetRequiredService<IOtpSender>());
    }
}
