using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence;

public sealed class OnePondyDbContext(DbContextOptions<OnePondyDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IOnePondyDbContext
{
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Provider settings shared by the app and the design-time factory used for migrations.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Entity configurations live in Persistence/Configurations (spec §18.1).
        builder.ApplyConfigurationsFromAssembly(typeof(OnePondyDbContext).Assembly);
    }
}
