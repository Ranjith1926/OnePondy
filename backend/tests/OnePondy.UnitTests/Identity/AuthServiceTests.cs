using System.Text;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;

namespace OnePondy.UnitTests.Identity;

public class AuthServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task Sends_a_code_and_stores_only_a_keyed_hash_of_it()
    {
        var harness = new AuthHarness();

        var result = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "98765 43210", None);

        Assert.True(result.IsSuccess);
        Assert.Equal("+91 ••••••3210", result.Value.MaskedDestination);
        var code = harness.Sender.LastCodes["+919876543210"];
        Assert.Matches("^[0-9]{6}$", code);
        var stored = harness.Db.OtpChallenges.Single();
        Assert.NotEqual(Encoding.UTF8.GetBytes(code), stored.CodeHash);
        Assert.True(harness.Hasher.Matches(code, stored.CodeHash));
    }

    [Fact]
    public async Task Rejects_invalid_destinations_and_non_Indian_SMS_numbers()
    {
        var harness = new AuthHarness();

        var invalid = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "12345", None);
        var foreign = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "+14155550100", None);
        var email = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Email, "visitor@example.com", None);

        Assert.Equal(AuthErrors.InvalidDestination, invalid.Error);
        Assert.Equal(AuthErrors.UnsupportedCountry, foreign.Error);
        Assert.True(email.IsSuccess);
    }

    [Fact]
    public async Task Enforces_the_resend_interval_and_the_per_destination_limit()
    {
        var harness = new AuthHarness();

        Assert.True((await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None)).IsSuccess);
        var tooSoon = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None);
        Assert.Equal("too_many_requests", tooSoon.Error?.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), tooSoon.Error?.RetryAfter);

        harness.Time.Advance(TimeSpan.FromSeconds(31));
        Assert.True((await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None)).IsSuccess);
        harness.Time.Advance(TimeSpan.FromSeconds(31));
        Assert.True((await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None)).IsSuccess);
        harness.Time.Advance(TimeSpan.FromSeconds(31));

        var limited = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None);
        Assert.Equal("too_many_requests", limited.Error?.Code);
        Assert.True(limited.Error?.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task Stops_sending_when_the_daily_cap_is_reached()
    {
        var harness = new AuthHarness(new OtpOptions { DailySendLimit = 2 });

        await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None);
        await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543211", None);
        var capped = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543212", None);

        Assert.Equal(AuthErrors.DailyLimitReached, capped.Error);
        Assert.False(harness.Sender.LastCodes.ContainsKey("+919876543212"));
    }

    [Fact]
    public async Task Reports_delivery_failures_honestly()
    {
        var harness = new AuthHarness();
        harness.Sender.Fail = true;

        var result = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None);

        Assert.Equal(AuthErrors.DeliveryUnavailable, result.Error);
    }

    [Fact]
    public async Task Signing_in_creates_the_account_once()
    {
        var harness = new AuthHarness();

        var first = await harness.SignInAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        var second = await harness.SignInAsync();

        Assert.True(first.User.IsNew);
        Assert.False(second.User.IsNew);
        Assert.Equal(first.User.Id, second.User.Id);
    }

    [Fact]
    public async Task Rejects_wrong_expired_and_reused_codes()
    {
        var harness = new AuthHarness();
        var requested = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None);
        var challengeId = requested.Value!.ChallengeId;
        var code = harness.Sender.LastCodes["+919876543210"];
        var wrong = code == "000000" ? "111111" : "000000";

        Assert.Equal(AuthErrors.InvalidCode, (await harness.Auth.VerifySignInCodeAsync(challengeId, wrong, null, None)).Error);
        Assert.True((await harness.Auth.VerifySignInCodeAsync(challengeId, code, null, None)).IsSuccess);
        Assert.Equal(AuthErrors.InvalidCode, (await harness.Auth.VerifySignInCodeAsync(challengeId, code, null, None)).Error);

        harness.Time.Advance(TimeSpan.FromSeconds(31));
        var next = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None);
        harness.Time.Advance(TimeSpan.FromMinutes(6));
        var expired = await harness.Auth.VerifySignInCodeAsync(
            next.Value!.ChallengeId,
            harness.Sender.LastCodes["+919876543210"],
            null,
            None);
        Assert.Equal(AuthErrors.CodeExpired, expired.Error);
    }

    [Fact]
    public async Task Locks_a_code_after_five_wrong_attempts()
    {
        var harness = new AuthHarness();
        var requested = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, "9876543210", None);
        var code = harness.Sender.LastCodes["+919876543210"];
        var wrong = code == "000000" ? "111111" : "000000";

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await harness.Auth.VerifySignInCodeAsync(requested.Value!.ChallengeId, wrong, null, None);
        }

        var fifth = await harness.Auth.VerifySignInCodeAsync(requested.Value!.ChallengeId, wrong, null, None);
        var thenCorrect = await harness.Auth.VerifySignInCodeAsync(requested.Value!.ChallengeId, code, null, None);

        Assert.Equal(AuthErrors.TooManyAttempts, fifth.Error);
        Assert.Equal(AuthErrors.TooManyAttempts, thenCorrect.Error);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_revokes_the_session()
    {
        var harness = new AuthHarness();
        var signedIn = await harness.SignInAsync();
        var sessionId = AuthHarness.SessionIdOf(signedIn);

        var refreshed = await harness.Auth.RefreshAsync(signedIn.RefreshToken, None);
        Assert.True(refreshed.IsSuccess);
        Assert.NotEqual(signedIn.RefreshToken, refreshed.Value.RefreshToken);

        var reused = await harness.Auth.RefreshAsync(signedIn.RefreshToken, None);
        Assert.Equal(AuthErrors.InvalidRefreshToken, reused.Error);
        Assert.False(await harness.Auth.IsSessionActiveAsync(sessionId, None));

        // The legitimate holder's newer token stops working too: the whole session is revoked.
        Assert.False((await harness.Auth.RefreshAsync(refreshed.Value.RefreshToken, None)).IsSuccess);
    }

    [Fact]
    public async Task Refresh_fails_after_the_session_expires()
    {
        var harness = new AuthHarness();
        var signedIn = await harness.SignInAsync();

        harness.Time.Advance(TimeSpan.FromDays(31));

        Assert.Equal(AuthErrors.InvalidRefreshToken, (await harness.Auth.RefreshAsync(signedIn.RefreshToken, None)).Error);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_immediately()
    {
        var harness = new AuthHarness();
        var signedIn = await harness.SignInAsync();
        var sessionId = AuthHarness.SessionIdOf(signedIn);

        await harness.Auth.SignOutAsync(sessionId, None);

        Assert.False(await harness.Auth.IsSessionActiveAsync(sessionId, None));
        Assert.False((await harness.Auth.RefreshAsync(signedIn.RefreshToken, None)).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task Rejects_unknown_refresh_tokens(string? token)
    {
        var harness = new AuthHarness();

        Assert.Equal(AuthErrors.InvalidRefreshToken, (await harness.Auth.RefreshAsync(token, None)).Error);
    }
}
