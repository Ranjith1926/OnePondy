namespace OnePondy.Domain.Identity;

/// <summary>
/// One link in a session's refresh-token chain. Only a keyed hash is stored. Each token can be used
/// once; presenting a rotated token again means it leaked, so the session is revoked.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public static RefreshToken Issue(Guid sessionId, byte[] tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SessionId = sessionId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };

    public bool IsConsumed => ConsumedAt is not null;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public void MarkRotated(Guid replacementId, DateTimeOffset now)
    {
        ConsumedAt = now;
        ReplacedByTokenId = replacementId;
    }
}
