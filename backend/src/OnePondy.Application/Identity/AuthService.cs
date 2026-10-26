using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Domain.Identity;

namespace OnePondy.Application.Identity;

public sealed record SessionTokens(
    AccessToken AccessToken,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserAccount User);

/// <summary>
/// Passwordless sign-in and session lifecycle (spec D1, D2): short-lived access tokens plus refresh
/// tokens that rotate on every use. Reusing a rotated token revokes the whole session.
/// </summary>
public sealed class AuthService(
    IOnePondyDbContext db,
    OtpChallengeService challenges,
    IUserAccountStore accounts,
    IAccessTokenIssuer accessTokens,
    ISecretHasher hasher,
    IOptions<SessionOptions> sessionOptions,
    TimeProvider time,
    ILogger<AuthService> logger)
{
    private readonly TimeSpan _refreshLifetime = sessionOptions.Value.RefreshTokenLifetime;

    public async Task<Result<OtpRequested>> RequestSignInCodeAsync(
        OtpChannel channel,
        string? destination,
        CancellationToken cancellationToken)
    {
        var normalized = challenges.NormalizeDestination(channel, destination);
        if (!normalized.IsSuccess)
        {
            return normalized.Error;
        }

        return await challenges.IssueAsync(channel, OtpPurpose.SignIn, normalized.Value, deliver: true, cancellationToken);
    }

    public async Task<Result<SessionTokens>> VerifySignInCodeAsync(
        Guid challengeId,
        string? code,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        var verified = await challenges.VerifyAsync(challengeId, OtpPurpose.SignIn, code, cancellationToken);
        if (!verified.IsSuccess)
        {
            return verified.Error;
        }

        var account = await accounts.FindOrCreateAsync(verified.Value.Channel, verified.Value.Destination, cancellationToken);
        var now = time.GetUtcNow();
        var session = UserSession.Start(account.Id, Truncate(deviceName, 120), now, _refreshLifetime);
        var (refreshToken, refreshPlain) = NewRefreshToken(session.Id, now);
        db.UserSessions.Add(session);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} signed in; session {SessionId} started", account.Id, session.Id);
        return new SessionTokens(
            accessTokens.Issue(account.Id, account.Roles, session.Id),
            refreshPlain,
            refreshToken.ExpiresAt,
            account);
    }

    public async Task<Result<SessionTokens>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 200)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var hash = hasher.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash.SequenceEqual(hash), cancellationToken);
        if (stored is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var session = await db.UserSessions.SingleAsync(s => s.Id == stored.SessionId, cancellationToken);
        var now = time.GetUtcNow();

        if (stored.IsConsumed)
        {
            session.Revoke(SessionRevocationReason.RefreshTokenReuse, now);
            await db.SaveChangesAsync(cancellationToken);
            AuthMetrics.RefreshTokenReuse.Add(1);
            logger.LogWarning("Rotated refresh token reused; session {SessionId} revoked", session.Id);
            return AuthErrors.InvalidRefreshToken;
        }

        if (stored.IsExpired(now) || !session.IsActive(now))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var account = await accounts.FindActiveAsync(session.UserId, cancellationToken);
        if (account is null)
        {
            session.Revoke(SessionRevocationReason.AccountDeleted, now);
            await db.SaveChangesAsync(cancellationToken);
            return AuthErrors.InvalidRefreshToken;
        }

        var (replacement, replacementPlain) = NewRefreshToken(session.Id, now);
        db.RefreshTokens.Add(replacement);
        stored.MarkRotated(replacement.Id, now);
        session.Extend(now, _refreshLifetime);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated this token at the same moment; let the client retry with its newer token.
            return AuthErrors.InvalidRefreshToken;
        }

        return new SessionTokens(
            accessTokens.Issue(account.Id, account.Roles, session.Id),
            replacementPlain,
            replacement.ExpiresAt,
            account);
    }

    public async Task SignOutAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.UserSessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(SessionRevocationReason.SignedOut, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Called for every authenticated request so sign-out and deletion take effect immediately.</summary>
    public Task<bool> IsSessionActiveAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.UserSessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.RevokedAt == null && s.ExpiresAt > now, cancellationToken);
    }

    private (RefreshToken Token, string Plain) NewRefreshToken(Guid sessionId, DateTimeOffset now)
    {
        var plain = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (RefreshToken.Issue(sessionId, hasher.Hash(plain), now, _refreshLifetime), plain);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
