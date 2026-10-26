using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OnePondy.Infrastructure.Identity;
using OnePondy.Infrastructure.Security;
using OnePondy.UnitTests.Identity;

namespace OnePondy.UnitTests.Infrastructure;

public class HmacSecretHasherTests
{
    private static HmacSecretHasher Create(string key) =>
        new(Options.Create(new SecretHashingOptions { HashingKey = key }));

    [Fact]
    public void Hashes_are_keyed_and_verifiable()
    {
        var hasher = Create(TestKeys.Hashing);
        var other = Create(Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('x', 32))));

        var hash = hasher.Hash("123456");

        Assert.True(hasher.Matches("123456", hash));
        Assert.False(hasher.Matches("123457", hash));
        Assert.NotEqual(hash, other.Hash("123456"));
    }

    [Fact]
    public void Fingerprints_are_short_and_stable()
    {
        var hasher = Create(TestKeys.Hashing);

        Assert.Equal(12, hasher.Fingerprint("+919876543210").Length);
        Assert.Equal(hasher.Fingerprint("+919876543210"), hasher.Fingerprint("+919876543210"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("c2hvcnQ=")]
    [InlineData("not base64!")]
    public void Rejects_weak_or_malformed_keys(string key) =>
        Assert.Throws<InvalidOperationException>(() => Create(key));
}

public class JwtAccessTokenIssuerTests
{
    [Fact]
    public async Task Issues_a_signed_short_lived_token_with_user_session_and_roles()
    {
        var now = new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero);
        var options = new JwtOptions { SigningKey = TestKeys.Signing };
        var issuer = new JwtAccessTokenIssuer(Options.Create(options), new FakeTimeProvider(now));
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var token = issuer.Issue(userId, ["customer"], sessionId);

        Assert.Equal(now.AddMinutes(15), token.ExpiresAt);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = options.CreateSecurityKey(),
            ValidateLifetime = false,
        });
        Assert.True(validation.IsValid, validation.Exception?.Message);
        Assert.Equal(userId.ToString(), validation.Claims["sub"]);
        Assert.Equal(sessionId.ToString(), validation.Claims["sid"]);
        Assert.Equal("customer", validation.Claims["role"]);
    }
}
