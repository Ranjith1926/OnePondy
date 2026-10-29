using System.Security.Cryptography;
using System.Text;

namespace OnePondy.Application.Payments;

/// <summary>
/// Razorpay signs each webhook with HMAC-SHA256 of the exact request body, keyed by the webhook secret, and sends
/// it as lower-case hex in <c>X-Razorpay-Signature</c>. Nothing in a webhook is trusted until this passes.
/// </summary>
public static class RazorpaySignature
{
    public static string Compute(string secret, ReadOnlySpan<byte> body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, body));
    }

    /// <summary>Compares in constant time, so the check does not leak how much of a forged signature was right.</summary>
    public static bool IsValid(string? secret, ReadOnlySpan<byte> body, string? signature)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Compute(secret, body));
        var supplied = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
