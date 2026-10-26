using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "OnePondy";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. For local development run " +
                $"'dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"<connection string>\" " +
                "--project src/OnePondy.Api' from the backend folder (see README).");
        }

        services.AddDbContext<OnePondyDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        return services;
    }
}
