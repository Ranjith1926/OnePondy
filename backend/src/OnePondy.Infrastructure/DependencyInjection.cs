using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OnePondy.Application.Businesses;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Application.Media;
using OnePondy.Application.Staff;
using OnePondy.Application.TripPlanner;
using OnePondy.Infrastructure.Businesses;
using OnePondy.Infrastructure.Identity;
using OnePondy.Infrastructure.Media;
using OnePondy.Infrastructure.Messaging;
using OnePondy.Infrastructure.Persistence;
using OnePondy.Infrastructure.Security;
using OnePondy.Infrastructure.TripPlanner;

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
        services.AddScoped<IStaffDirectory, StaffDirectory>();

        services.AddOptions<MediaStorageOptions>()
            .Bind(configuration.GetSection(MediaStorageOptions.SectionName))
            .PostConfigure(options => options.RootPath ??= Path.Combine(environment.ContentRootPath, "App_Data", "media"));
        services.AddSingleton<IMediaStorage, LocalMediaStorage>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();

        services.AddOptions<BusinessDocumentStorageOptions>()
            .Bind(configuration.GetSection(BusinessDocumentStorageOptions.SectionName))
            .PostConfigure(options => options.RootPath ??= Path.Combine(environment.ContentRootPath, "App_Data", "business-documents"));
        services.AddSingleton<IBusinessDocumentStorage, LocalBusinessDocumentStorage>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => KeyMaterial.IsValid(options.SigningKey), MissingKeyMessage("Auth:Jwt:SigningKey"));
        services.AddOptions<SecretHashingOptions>()
            .Bind(configuration.GetSection(SecretHashingOptions.SectionName))
            .Validate(options => KeyMaterial.IsValid(options.HashingKey), MissingKeyMessage("Auth:HashingKey"));
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ISecretHasher, HmacSecretHasher>();

        AddOtpDelivery(services, configuration, environment);
        AddTripPlannerProvider(services, configuration);
        return services;
    }

    private static void AddOtpDelivery(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var delivery = configuration["Auth:Otp:Delivery"];
        if (string.Equals(delivery, "Twilio", StringComparison.OrdinalIgnoreCase))
        {
            var twilio = configuration.GetSection(TwilioOptions.SectionName).Get<TwilioOptions>() ?? new TwilioOptions();
            if (!twilio.IsConfigured)
            {
                throw new InvalidOperationException(
                    "Auth:Otp:Delivery 'Twilio' needs Twilio:AccountSid, Twilio:AuthToken and either Twilio:From or " +
                    "Twilio:MessagingServiceSid. For local development use 'dotnet user-secrets set' with " +
                    "--project src/OnePondy.Api (see README).");
            }

            services.AddOptions<TwilioOptions>().Bind(configuration.GetSection(TwilioOptions.SectionName));
            services.AddHttpClient<IOtpSender, TwilioOtpSender>(client => client.Timeout = TimeSpan.FromSeconds(15));
            return;
        }

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

    /// <summary>
    /// Falls back to a planner that reports itself unavailable until an API key is configured (spec
    /// D5), the same way <see cref="AddOtpDelivery"/> falls back for one-time codes.
    /// </summary>
    private static void AddTripPlannerProvider(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GeminiOptions>().Bind(configuration.GetSection(GeminiOptions.SectionName));

        if (string.IsNullOrWhiteSpace(configuration[$"{GeminiOptions.SectionName}:ApiKey"]))
        {
            services.AddSingleton<IItineraryPlanner, UnavailableItineraryPlanner>();
            return;
        }

        services.AddHttpClient<IItineraryPlanner, GeminiItineraryPlanner>((provider, client) =>
        {
            client.Timeout = provider.GetRequiredService<IOptions<GeminiOptions>>().Value.Timeout;
        });
    }

    private static string MissingKeyMessage(string key) =>
        $"{key} must be a base64-encoded key of at least {KeyMaterial.MinimumBytes} bytes. For local development " +
        $"run 'dotnet user-secrets set \"{key}\" \"<key>\" --project src/OnePondy.Api' (see README).";
}
