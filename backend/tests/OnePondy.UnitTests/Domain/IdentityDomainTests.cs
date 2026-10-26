using OnePondy.Domain.Identity;

namespace OnePondy.UnitTests.Domain;

public class ContactNormalizerTests
{
    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("98765 43210", "+919876543210")]
    [InlineData("098765-43210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("+1 415 555 0100", "+14155550100")]
    public void Normalizes_phone_numbers_to_E164(string input, string expected) =>
        Assert.Equal(expected, ContactNormalizer.NormalizePhone(input));

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("5876543210")]
    [InlineData("+0123456789")]
    [InlineData("phone")]
    public void Rejects_invalid_phone_numbers(string input) => Assert.Null(ContactNormalizer.NormalizePhone(input));

    [Theory]
    [InlineData(" Asha@Example.COM ", "asha@example.com")]
    [InlineData("a.b+tag@mail.example.in", "a.b+tag@mail.example.in")]
    public void Normalizes_email_addresses(string input, string expected) =>
        Assert.Equal(expected, ContactNormalizer.NormalizeEmail(input));

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("two words@example.com")]
    public void Rejects_invalid_email_addresses(string input) => Assert.Null(ContactNormalizer.NormalizeEmail(input));
}

public class OtpChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] CorrectHash = [1, 2, 3];

    private static OtpChallenge NewChallenge() => OtpChallenge.Create(
        OtpChannel.Sms,
        OtpPurpose.SignIn,
        "+919876543210",
        CorrectHash,
        Now,
        TimeSpan.FromMinutes(5));

    private static Func<byte[], bool> Code(bool correct) => _ => correct;

    [Fact]
    public void Verifies_once_and_cannot_be_reused()
    {
        var challenge = NewChallenge();

        Assert.Equal(OtpVerificationResult.Verified, challenge.Verify(Code(true), Now.AddMinutes(1), maxAttempts: 5));
        Assert.Equal(OtpVerificationResult.AlreadyUsed, challenge.Verify(Code(true), Now.AddMinutes(1), maxAttempts: 5));
    }

    [Fact]
    public void Locks_after_the_maximum_number_of_wrong_codes()
    {
        var challenge = NewChallenge();

        for (var attempt = 1; attempt < 5; attempt++)
        {
            Assert.Equal(OtpVerificationResult.Invalid, challenge.Verify(Code(false), Now, maxAttempts: 5));
        }

        Assert.Equal(OtpVerificationResult.TooManyAttempts, challenge.Verify(Code(false), Now, maxAttempts: 5));
        Assert.Equal(OtpVerificationResult.TooManyAttempts, challenge.Verify(Code(true), Now, maxAttempts: 5));
    }

    [Fact]
    public void Expires_after_its_lifetime()
    {
        var challenge = NewChallenge();

        Assert.Equal(OtpVerificationResult.Expired, challenge.Verify(Code(true), Now.AddMinutes(5), maxAttempts: 5));
    }
}

public class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Revocation_is_permanent_and_keeps_the_first_reason()
    {
        var session = UserSession.Start(Guid.NewGuid(), "Pixel 7", Now, TimeSpan.FromDays(30));

        session.Revoke(SessionRevocationReason.RefreshTokenReuse, Now);
        session.Revoke(SessionRevocationReason.SignedOut, Now.AddMinutes(1));

        Assert.False(session.IsActive(Now));
        Assert.Equal(SessionRevocationReason.RefreshTokenReuse, session.RevokedReason);
    }

    [Fact]
    public void Extending_slides_the_expiry()
    {
        var session = UserSession.Start(Guid.NewGuid(), null, Now, TimeSpan.FromDays(30));

        session.Extend(Now.AddDays(20), TimeSpan.FromDays(30));

        Assert.True(session.IsActive(Now.AddDays(45)));
        Assert.Equal(Now.AddDays(50), session.ExpiresAt);
    }
}
