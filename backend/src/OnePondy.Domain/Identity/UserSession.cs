namespace OnePondy.Domain.Identity;

public enum SessionRevocationReason
{
    SignedOut,
    RefreshTokenReuse,
    AccountDeleted,
}

/// <summary>A signed-in device. Access tokens carry the session id so revocation takes effect at once.</summary>
public sealed class UserSession
{
    private UserSession()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string? DeviceName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastRefreshedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionRevocationReason? RevokedReason { get; private set; }

    public static UserSession Start(Guid userId, string? deviceName, DateTimeOffset now, TimeSpan lifetime) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            DeviceName = deviceName,
            CreatedAt = now,
            LastRefreshedAt = now,
            ExpiresAt = now + lifetime,
        };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>Sliding expiry: each successful refresh keeps the session alive for another lifetime.</summary>
    public void Extend(DateTimeOffset now, TimeSpan lifetime)
    {
        LastRefreshedAt = now;
        ExpiresAt = now + lifetime;
    }

    public void Revoke(SessionRevocationReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }
}
