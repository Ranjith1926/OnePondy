namespace OnePondy.Application.Common;

/// <summary>Keyed hashing for one-time codes and refresh tokens, so stored values are useless if leaked.</summary>
public interface ISecretHasher
{
    byte[] Hash(string secret);

    /// <summary>Constant-time comparison of a submitted secret with a stored hash.</summary>
    bool Matches(string secret, byte[] hash);

    /// <summary>Short, non-reversible identifier for logs, so phone numbers and emails never appear in them.</summary>
    string Fingerprint(string value);
}
