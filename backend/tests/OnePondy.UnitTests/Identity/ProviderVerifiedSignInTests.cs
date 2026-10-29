using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;

namespace OnePondy.UnitTests.Identity;

public class ProviderVerifiedSignInTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static AuthHarness Harness()
    {
        var harness = new AuthHarness();
        harness.Sender.ProviderMode = true;
        return harness;
    }

    [Fact]
    public async Task Asks_the_provider_to_start_a_verification_instead_of_sending_our_own_code()
    {
        var harness = Harness();

        var result = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "98765 43210", None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["+919876543210"], harness.Sender.StartedVerifications);
        Assert.Empty(harness.Sender.LastCodes);
    }

    [Fact]
    public async Task Signs_in_when_the_provider_approves_the_code()
    {
        var harness = Harness();
        var requested = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "98765 43210", None);

        var session = await harness.Auth.VerifySignInCodeAsync(requested.Value!.ChallengeId, "654321", "phone", None);

        Assert.True(session.IsSuccess);
    }

    [Fact]
    public async Task A_code_the_provider_rejects_counts_as_an_attempt_and_is_locked_after_the_limit()
    {
        var harness = Harness();
        var requested = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "98765 43210", None);
        var maxAttempts = new OtpOptions().MaxAttempts;

        for (var i = 0; i < maxAttempts; i++)
        {
            Assert.False((await harness.Auth.VerifySignInCodeAsync(requested.Value!.ChallengeId, "000000", "phone", None)).IsSuccess);
        }

        var late = await harness.Auth.VerifySignInCodeAsync(requested.Value!.ChallengeId, "654321", "phone", None);

        Assert.False(late.IsSuccess);
        Assert.Equal(maxAttempts, harness.Sender.Checks);
    }

    [Fact]
    public async Task An_expired_challenge_is_refused_without_asking_the_provider()
    {
        var harness = Harness();
        var requested = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "98765 43210", None);
        harness.Time.Advance(TimeSpan.FromMinutes(6));

        var result = await harness.Auth.VerifySignInCodeAsync(requested.Value!.ChallengeId, "654321", "phone", None);

        Assert.Equal(AuthErrors.CodeExpired, result.Error);
        Assert.Equal(0, harness.Sender.Checks);
    }

    [Fact]
    public async Task A_provider_that_cannot_start_a_verification_reports_delivery_unavailable()
    {
        var harness = Harness();
        harness.Sender.Fail = true;

        var result = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "98765 43210", None);

        Assert.Equal(AuthErrors.DeliveryUnavailable, result.Error);
    }
}
