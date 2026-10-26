using Microsoft.EntityFrameworkCore;
using OnePondy.Domain.Identity;

namespace OnePondy.Application.Common;

/// <summary>The persistence surface the application layer uses; implemented by Infrastructure.</summary>
public interface IOnePondyDbContext
{
    DbSet<OtpChallenge> OtpChallenges { get; }

    DbSet<UserSession> UserSessions { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
