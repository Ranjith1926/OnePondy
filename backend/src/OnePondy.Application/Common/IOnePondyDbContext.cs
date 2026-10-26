using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Places;

namespace OnePondy.Application.Common;

/// <summary>The persistence surface the application layer uses; implemented by Infrastructure.</summary>
public interface IOnePondyDbContext
{
    DbSet<OtpChallenge> OtpChallenges { get; }

    DbSet<UserSession> UserSessions { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Place> Places { get; }

    DbSet<PlaceCategory> PlaceCategories { get; }

    DbSet<PlaceMedia> PlaceMedia { get; }

    DbSet<ModuleFlag> ModuleFlags { get; }

    DbSet<AppSetting> AppSettings { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    /// <summary>
    /// Makes the next save fail with a concurrency error unless the row still has this version, so an
    /// edit based on stale data is rejected instead of overwriting someone else's change.
    /// </summary>
    void ExpectVersion(Place place, uint version);

    /// <summary>For changes that span several saves, such as Identity role updates plus their audit entry.</summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
