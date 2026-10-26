using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;

namespace OnePondy.Infrastructure.Security;

public sealed class SecretHashingOptions
{
    public const string SectionName = "Auth";

    /// <summary>Base64-encoded key of at least 32 bytes (config key <c>Auth:HashingKey</c>).</summary>
    public string HashingKey { get; set; } = string.Empty;
}

/// <summary>
/// HMAC-SHA256 with a server-side key. Plain hashes of 6-digit codes could be brute-forced offline
/// if the database leaked; keyed hashes cannot without the key.
/// </summary>
public sealed class HmacSecretHasher(IOptions<SecretHashingOptions> options) : ISecretHasher
{
    private readonly byte[] _key = KeyMaterial.Decode(options.Value.HashingKey);

    public byte[] Hash(string secret) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(secret));

    public bool Matches(string secret, byte[] hash) => CryptographicOperations.FixedTimeEquals(Hash(secret), hash);

    public string Fingerprint(string value) => Convert.ToHexStringLower(Hash("fingerprint:" + value))[..12];
}

public static class KeyMaterial
{
    public const int MinimumBytes = 32;

    public static bool IsValid(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(base64).Length >= MinimumBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static byte[] Decode(string base64) =>
        IsValid(base64)
            ? Convert.FromBase64String(base64)
            : throw new InvalidOperationException($"Key material must be base64 encoding at least {MinimumBytes} bytes.");
}
