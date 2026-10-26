using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Infrastructure.Identity;
using OnePondy.Infrastructure.Messaging;
using OnePondy.Infrastructure.Persistence;
using OnePondy.Infrastructure.Security;

namespace OnePondy.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "OnePondy";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. For local development run " +
                $"'dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"<connection string>\" " +
                "--project src/OnePondy.Api' from the backend folder (see README).");
        }

        services.AddDbContext<OnePondyDbContext>(options => OnePondyDbContext.Configure(options, connectionString));
        services.AddScoped<IOnePondyDbContext>(provider => provider.GetRequiredService<OnePondyDbContext>());

        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<OnePondyDbContext>();
        services.AddScoped<IUserAccountStore, UserAccountStore>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => KeyMaterial.IsValid(options.SigningKey), MissingKeyMessage("Auth:Jwt:SigningKey"));
        services.AddOptions<SecretHashingOptions>()
            .Bind(configuration.GetSection(SecretHashingOptions.SectionName))
            .Validate(options => KeyMaterial.IsValid(options.HashingKey), MissingKeyMessage("Auth:HashingKey"));
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ISecretHasher, HmacSecretHasher>();

        AddOtpDelivery(services, configuration, environment);
        return services;
    }

    private static void AddOtpDelivery(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var delivery = configuration["Auth:Otp:Delivery"];
        if (!string.Equals(delivery, "Log", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IOtpSender, UnavailableOtpSender>();
            return;
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "Auth:Otp:Delivery 'Log' writes one-time codes to the log and is only allowed in Development.");
        }

        services.AddSingleton<IOtpSender, LoggingOtpSender>();
    }

    private static string MissingKeyMessage(string key) =>
        $"{key} must be a base64-encoded key of at least {KeyMaterial.MinimumBytes} bytes. For local development " +
        $"run 'dotnet user-secrets set \"{key}\" \"<key>\" --project src/OnePondy.Api' (see README).";
}
