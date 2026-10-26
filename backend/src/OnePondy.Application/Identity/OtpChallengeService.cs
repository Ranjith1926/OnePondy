using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Domain.Identity;

namespace OnePondy.Application.Identity;

public sealed record OtpRequested(
    Guid ChallengeId,
    DateTimeOffset ExpiresAt,
    TimeSpan ResendAfter,
    string MaskedDestination);

/// <summary>Issues and verifies one-time codes, enforcing per-destination and daily limits.</summary>
public sealed class OtpChallengeService(
    IOnePondyDbContext db,
    IOtpSender sender,
    ISecretHasher hasher,
    IOptions<OtpOptions> options,
    TimeProvider time,
    ILogger<OtpChallengeService> logger)
{
    private readonly OtpOptions _options = options.Value;

    /// <summary>Normalises and validates a destination for the channel, or returns an error.</summary>
    public Result<string> NormalizeDestination(OtpChannel channel, string? destination)
    {
        if (channel == OtpChannel.Email)
        {
            return ContactNormalizer.NormalizeEmail(destination) is { } email
                ? email
                : AuthErrors.InvalidDestination;
        }

        if (ContactNormalizer.NormalizePhone(destination) is not { } phone)
        {
            return AuthErrors.InvalidDestination;
        }

        return _options.SmsCallingCodes.Any(code => ContactNormalizer.HasCallingCode(phone, code))
            ? phone
            : AuthErrors.UnsupportedCountry;
    }

    /// <param name="deliver">
    /// False records the challenge without sending anything, so a public form gives the same
    /// response whether or not an account exists.
    /// </param>
    public async Task<Result<OtpRequested>> IssueAsync(
        OtpChannel channel,
        OtpPurpose purpose,
        string destination,
        bool deliver,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var windowStart = now - _options.DestinationWindow;
        var recent = await db.OtpChallenges
            .Where(c => c.Destination == destination && c.CreatedAt >= windowStart)
            .Select(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        if (recent.Count > 0)
        {
            var resendAt = recent.Max() + _options.ResendInterval;
            if (now < resendAt)
            {
                return AuthErrors.TooManyRequests(resendAt - now);
            }
        }

        if (recent.Count >= _options.MaxPerDestination)
        {
            AuthMetrics.CodeRequestsLimited.Add(1);
            logger.LogWarning(
                "Code requests limited for destination {DestinationFingerprint}",
                hasher.Fingerprint(destination));
            return AuthErrors.TooManyRequests(recent.Min() + _options.DestinationWindow - now);
        }

        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var sentToday = await db.OtpChallenges.CountAsync(c => c.CreatedAt >= dayStart, cancellationToken);
        if (sentToday >= _options.DailySendLimit)
        {
            AuthMetrics.DailyLimitReached.Add(1);
            logger.LogCritical("Daily one-time code limit of {Limit} reached", _options.DailySendLimit);
            return AuthErrors.DailyLimitReached;
        }

        var code = GenerateCode(_options.CodeLength);
        var challenge = OtpChallenge.Create(channel, purpose, destination, hasher.Hash(code), now, _options.Lifetime);
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);

        if (deliver)
        {
            try
            {
                await sender.SendCodeAsync(channel, destination, code, purpose, cancellationToken);
            }
            catch (OtpDeliveryException exception)
            {
                logger.LogError(
                    exception,
                    "Could not send a {Channel} code to {DestinationFingerprint}",
                    channel,
                    hasher.Fingerprint(destination));
                return AuthErrors.DeliveryUnavailable;
            }

            AuthMetrics.CodesSent.Add(1);
        }

        return new OtpRequested(challenge.Id, challenge.ExpiresAt, _options.ResendInterval, Mask(channel, destination));
    }

    public async Task<Result<OtpChallenge>> VerifyAsync(
        Guid challengeId,
        OtpPurpose purpose,
        string? code,
        CancellationToken cancellationToken)
    {
        var challenge = await db.OtpChallenges
            .SingleOrDefaultAsync(c => c.Id == challengeId && c.Purpose == purpose, cancellationToken);
        if (challenge is null)
        {
            return AuthErrors.InvalidCode;
        }

        var submitted = code?.Trim() ?? string.Empty;
        var result = challenge.Verify(hash => hasher.Matches(submitted, hash), time.GetUtcNow(), _options.MaxAttempts);
        await db.SaveChangesAsync(cancellationToken);

        return result switch
        {
            OtpVerificationResult.Verified => challenge,
            OtpVerificationResult.Expired => AuthErrors.CodeExpired,
            OtpVerificationResult.TooManyAttempts => AuthErrors.TooManyAttempts,
            _ => AuthErrors.InvalidCode,
        };
    }

    private static string GenerateCode(int length) =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, length)).ToString($"D{length}");

    /// <summary>Shows enough to recognise the destination without exposing it, e.g. "+91 ••••••3210".</summary>
    public static string Mask(OtpChannel channel, string destination)
    {
        if (channel == OtpChannel.Sms)
        {
            var visible = destination[^4..];
            var prefix = destination.StartsWith("+91", StringComparison.Ordinal) ? "+91 " : string.Empty;
            return $"{prefix}••••••{visible}";
        }

        var at = destination.IndexOf('@', StringComparison.Ordinal);
        return at <= 1 ? $"•{destination[at..]}" : $"{destination[0]}•••{destination[at..]}";
    }
}
