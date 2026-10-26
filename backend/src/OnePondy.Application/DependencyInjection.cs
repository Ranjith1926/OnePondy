using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OnePondy.Application.Identity;

namespace OnePondy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<SessionOptions>(configuration.GetSection(SessionOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAccountDeletionGuard, NoCommitmentsDeletionGuard>();

        services.AddScoped<OtpChallengeService>();
        services.AddScoped<AuthService>();
        services.AddScoped<AccountService>();
        services.AddScoped<AuthDataRetention>();

        return services;
    }
}
