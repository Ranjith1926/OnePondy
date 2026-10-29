using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;

namespace OnePondy.UnitTests.Identity;

public class AccountServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Phone = "+919876543210";

    [Fact]
    public async Task Deleting_requires_a_code_sent_to_the_accounts_own_number()
    {
        var harness = new AuthHarness();
        var signedIn = await harness.SignInAsync();
        var sessionId = AuthHarness.SessionIdOf(signedIn);
        harness.Time.Advance(TimeSpan.FromMinutes(1));

        var requested = await harness.AccountService.RequestDeletionCodeAsync(signedIn.User.Id, None);
        var error = await harness.AccountService.DeleteAccountAsync(
            signedIn.User.Id,
            requested.Value!.ChallengeId,
            harness.Sender.LastCodes[Phone],
            None);

        Assert.Null(error);
        Assert.True(harness.Accounts.IsDeleted(signedIn.User.Id));
        Assert.False(await harness.Auth.IsSessionActiveAsync(sessionId, None));
        Assert.Contains(Phone, harness.Sender.DeletionConfirmations);
    }

    [Fact]
    public async Task A_sign_in_code_cannot_be_used_to_delete()
    {
        var harness = new AuthHarness();
        var signedIn = await harness.SignInAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        var signInCode = await harness.Auth.RequestSignInCodeAsync(OtpChannel.Sms, Phone, None);

        var error = await harness.AccountService.DeleteAccountAsync(
            signedIn.User.Id,
            signInCode.Value!.ChallengeId,
            harness.Sender.LastCodes[Phone],
            None);

        Assert.Equal(AuthErrors.InvalidCode, error);
        Assert.False(harness.Accounts.IsDeleted(signedIn.User.Id));
    }

    [Fact]
    public async Task No_deletion_code_is_sent_while_activity_is_in_progress()
    {
        var harness = new AuthHarness(deletionGuard: new BlockingDeletionGuard("booking_in_progress"));
        var signedIn = await harness.SignInAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        var signInCode = harness.Sender.LastCodes[Phone];

        var requested = await harness.AccountService.RequestDeletionCodeAsync(signedIn.User.Id, None);

        Assert.Equal("deletion_blocked", requested.Error?.Code);
        Assert.Equal(["booking_in_progress"], requested.Error?.Reasons);
        Assert.Equal(signInCode, harness.Sender.LastCodes[Phone]);
    }

    [Fact]
    public async Task Deletion_still_waits_for_activity_that_started_after_the_code_was_sent()
    {
        var guard = new ToggleDeletionGuard();
        var harness = new AuthHarness(deletionGuard: guard);
        var signedIn = await harness.SignInAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(1));
        var requested = await harness.AccountService.RequestDeletionCodeAsync(signedIn.User.Id, None);
        guard.Reasons = ["booking_in_progress"];

        var error = await harness.AccountService.DeleteAccountAsync(
            signedIn.User.Id,
            requested.Value!.ChallengeId,
            harness.Sender.LastCodes[Phone],
            None);

        Assert.Equal("deletion_blocked", error?.Code);
        Assert.Equal(["booking_in_progress"], error?.Reasons);
        Assert.False(harness.Accounts.IsDeleted(signedIn.User.Id));
    }

    [Fact]
    public async Task The_public_form_sends_nothing_for_unknown_numbers_but_answers_the_same()
    {
        var harness = new AuthHarness();

        var result = await harness.AccountService.RequestDeletionCodeForContactAsync(OtpChannel.Sms, "9876500000", None);

        Assert.True(result.IsSuccess);
        Assert.Empty(harness.Sender.LastCodes);
    }

    [Fact]
    public async Task The_public_form_deletes_an_existing_account()
    {
        var harness = new AuthHarness();
        var signedIn = await harness.SignInAsync();
        harness.Time.Advance(TimeSpan.FromMinutes(1));

        var requested = await harness.AccountService.RequestDeletionCodeForContactAsync(OtpChannel.Sms, "9876543210", None);
        var error = await harness.AccountService.ConfirmDeletionForContactAsync(
            requested.Value!.ChallengeId,
            harness.Sender.LastCodes[Phone],
            None);

        Assert.Null(error);
        Assert.True(harness.Accounts.IsDeleted(signedIn.User.Id));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, "de")]
    public async Task Rejects_invalid_profile_changes(string? displayName, string? language)
    {
        var harness = new AuthHarness();
        var signedIn = await harness.SignInAsync();

        var result = await harness.AccountService.UpdateProfileAsync(signedIn.User.Id, displayName, language, None);

        Assert.Equal("invalid_profile", result.Error?.Code);
    }

    [Fact]
    public async Task Updates_the_display_name()
    {
        var harness = new AuthHarness();
        var signedIn = await harness.SignInAsync();

        var result = await harness.AccountService.UpdateProfileAsync(signedIn.User.Id, "  Asha  ", null, None);

        Assert.Equal("Asha", result.Value?.DisplayName);
        Assert.Equal(Phone, result.Value?.PhoneNumber);
    }
}
