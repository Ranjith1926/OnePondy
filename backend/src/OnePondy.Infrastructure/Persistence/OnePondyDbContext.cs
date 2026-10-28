using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Itineraries;
using OnePondy.Domain.Places;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence;

public sealed class OnePondyDbContext(DbContextOptions<OnePondyDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IOnePondyDbContext
{
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Place> Places => Set<Place>();

    public DbSet<PlaceCategory> PlaceCategories => Set<PlaceCategory>();

    public DbSet<PlaceMedia> PlaceMedia => Set<PlaceMedia>();

    public DbSet<ModuleFlag> ModuleFlags => Set<ModuleFlag>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<SavedPlace> SavedPlaces => Set<SavedPlace>();

    public DbSet<Itinerary> Itineraries => Set<Itinerary>();

    /// <summary>Provider settings shared by the app and the design-time factory used for migrations.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    public void ExpectVersion(Place place, uint version) => Entry(place).Property(p => p.Version).OriginalValue = version;

    public void MarkAdded<TEntity>(TEntity entity)
        where TEntity : class =>
        Entry(entity).State = EntityState.Added;

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Entity configurations live in Persistence/Configurations (spec §18.1).
        builder.ApplyConfigurationsFromAssembly(typeof(OnePondyDbContext).Assembly);
    }
}
