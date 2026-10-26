using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnePondy.Infrastructure;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.UnitTests.Infrastructure;

public class DependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_without_connection_string_fails_with_setup_guidance()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("ConnectionStrings:OnePondy", exception.Message);
        Assert.Contains("user-secrets", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_registers_DbContext_with_the_PostgreSQL_provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OnePondy"] = "Host=localhost;Database=onepondy_test;Username=test",
            })
            .Build();

        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OnePondyDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }
}
