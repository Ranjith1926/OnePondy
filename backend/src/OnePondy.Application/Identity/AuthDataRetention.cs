using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;

namespace OnePondy.Application.Identity;

public sealed record AuthDataPurged(int Challenges, int RefreshTokens, int Sessions);

/// <summary>Deletes short-lived authentication data on the schedule in docs/data-retention.md.</summary>
public sealed class AuthDataRetention(IOnePondyDbContext db, TimeProvider time)
{
    public static readonly TimeSpan ChallengeRetention = TimeSpan.FromDays(1);
    public static readonly TimeSpan RefreshTokenRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan SessionRetention = TimeSpan.FromDays(90);

    public async Task<AuthDataPurged> PurgeAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var challengeCutoff = now - ChallengeRetention;
        var tokenCutoff = now - RefreshTokenRetention;
        var sessionCutoff = now - SessionRetention;

        var challenges = await db.OtpChallenges
            .Where(c => c.CreatedAt < challengeCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var tokens = await db.RefreshTokens
            .Where(t => t.ExpiresAt < tokenCutoff || t.ConsumedAt < tokenCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Deleting a session also deletes its remaining refresh tokens (cascade).
        var sessions = await db.UserSessions
            .Where(s => s.RevokedAt < sessionCutoff || s.ExpiresAt < sessionCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return new AuthDataPurged(challenges, tokens, sessions);
    }
}
