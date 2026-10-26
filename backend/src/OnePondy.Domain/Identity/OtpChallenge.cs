namespace OnePondy.Domain.Identity;

public enum OtpChannel
{
    Sms = 1,
    Email = 2,
}

public enum OtpPurpose
{
    SignIn = 1,
    AccountDeletion = 2,

    /// <summary>Web console sign-in; codes are only sent to staff accounts.</summary>
    StaffSignIn = 3,
}

public enum OtpVerificationResult
{
    Verified,
    Invalid,
    Expired,
    TooManyAttempts,
    AlreadyUsed,
}

/// <summary>
/// A one-time code sent to a phone number or email address. Only a keyed hash of the code is
/// stored, and challenges are purged after a day (see docs/data-retention.md).
/// </summary>
public sealed class OtpChallenge
{
    private OtpChallenge()
    {
    }

    public Guid Id { get; private set; }

    public OtpChannel Channel { get; private set; }

    public OtpPurpose Purpose { get; private set; }

    /// <summary>Normalised phone number (E.164) or email address.</summary>
    public string Destination { get; private set; } = string.Empty;

    public byte[] CodeHash { get; private set; } = [];

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public static OtpChallenge Create(
        OtpChannel channel,
        OtpPurpose purpose,
        string destination,
        byte[] codeHash,
        DateTimeOffset now,
        TimeSpan lifetime) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Channel = channel,
            Purpose = purpose,
            Destination = destination,
            CodeHash = codeHash,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };

    /// <summary>
    /// Checks a submitted code. Every check of an open challenge counts as an attempt, and a
    /// verified challenge cannot be used again.
    /// </summary>
    public OtpVerificationResult Verify(Func<byte[], bool> codeMatches, DateTimeOffset now, int maxAttempts)
    {
        if (ConsumedAt is not null)
        {
            return OtpVerificationResult.AlreadyUsed;
        }

        if (now >= ExpiresAt)
        {
            return OtpVerificationResult.Expired;
        }

        if (Attempts >= maxAttempts)
        {
            return OtpVerificationResult.TooManyAttempts;
        }

        Attempts++;
        if (!codeMatches(CodeHash))
        {
            return Attempts >= maxAttempts
                ? OtpVerificationResult.TooManyAttempts
                : OtpVerificationResult.Invalid;
        }

        ConsumedAt = now;
        return OtpVerificationResult.Verified;
    }
}
