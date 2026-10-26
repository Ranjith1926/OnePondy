using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Configuration;
using OnePondy.Application.Identity;
using OnePondy.Application.Staff;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Identity;
using OnePondy.UnitTests.Identity;

namespace OnePondy.UnitTests.Content;

public class ConfigServiceTests
{
    private readonly ContentHarness _harness = new();

    [Fact]
    public async Task Every_module_is_coming_soon_until_changed()
    {
        var config = await _harness.Config.GetAsync(default);

        Assert.Equal(ModuleKeys.All, config.Modules.Keys);
        Assert.All(config.Modules.Values, status => Assert.Equal(ModuleStatus.ComingSoon, status));
        Assert.Equal("0.1.0", config.MinSupportedAppVersion);
    }

    [Fact]
    public async Task Changes_show_in_the_public_config_and_the_audit_log()
    {
        await _harness.Config.SetModuleStatusAsync("food", ModuleStatus.Hidden, _harness.Administrator, default);
        await _harness.Config.SetMinSupportedAppVersionAsync(" 0.2.0 ", _harness.Administrator, default);

        var config = await _harness.Config.GetAsync(default);
        Assert.Equal(ModuleStatus.Hidden, config.Modules["food"]);
        Assert.Equal("0.2.0", config.MinSupportedAppVersion);
        Assert.Equal(
            [AuditActions.ModuleFlagChanged, AuditActions.MinAppVersionChanged],
            _harness.Db.AuditEntries.OrderBy(e => e.Id).Select(e => e.Action));
    }

    [Fact]
    public async Task Unknown_modules_and_bad_versions_are_rejected()
    {
        Assert.Equal("unknown_module", (await _harness.Config.SetModuleStatusAsync("casino", ModuleStatus.Available, _harness.Administrator, default)).Error!.Code);
        Assert.Equal("validation_failed", (await _harness.Config.SetMinSupportedAppVersionAsync("v2", _harness.Administrator, default)).Error!.Code);
    }
}

public class StaffServiceTests
{
    private readonly ContentHarness _harness = new();

    [Fact]
    public async Task Adding_staff_by_email_grants_the_role_and_audits_it()
    {
        var added = (await _harness.StaffService.AddAsync(" Priya@OnePondy.test ", RoleNames.SupportAgent, _harness.Administrator, default)).Value!;

        Assert.Equal(RoleNames.SupportAgent, added.Role);
        Assert.Equal("priya@onepondy.test", added.Email);
        Assert.Contains(_harness.Db.AuditEntries, e => e.Action == AuditActions.StaffRoleGranted && e.EntityId == added.UserId.ToString());
    }

    [Fact]
    public async Task Changing_a_role_signs_the_person_out_everywhere()
    {
        var session = UserSession.Start(_harness.Editor, "Laptop", _harness.Time.GetUtcNow(), TimeSpan.FromDays(30));
        _harness.Db.UserSessions.Add(session);
        await _harness.Db.SaveChangesAsync();

        await _harness.StaffService.ChangeRoleAsync(_harness.Editor, RoleNames.SupportAgent, _harness.Administrator, default);

        Assert.Equal(SessionRevocationReason.RoleChanged, session.RevokedReason);
    }

    [Fact]
    public async Task Administrators_cannot_lock_themselves_or_everyone_out()
    {
        Assert.Equal("own_access", (await _harness.StaffService.RemoveAsync(_harness.Administrator, _harness.Administrator, default))!.Code);

        // A second administrator may not remove the last other one either.
        var other = _harness.AddStaff("second@onepondy.test", RoleNames.Administrator);
        Assert.Null(await _harness.StaffService.RemoveAsync(other, _harness.Administrator, default));
        Assert.Equal(
            "own_access",
            (await _harness.StaffService.ChangeRoleAsync(_harness.Administrator, RoleNames.ContentEditor, _harness.Administrator, default)).Error!.Code);

        var third = _harness.AddStaff("third@onepondy.test", RoleNames.ContentEditor);
        Assert.Equal(
            "last_administrator",
            (await _harness.StaffService.ChangeRoleAsync(_harness.Administrator, RoleNames.ContentEditor, third, default)).Error!.Code);
    }

    [Fact]
    public async Task Roles_must_be_staff_roles()
    {
        var error = (await _harness.StaffService.AddAsync("someone@", "customer", _harness.Administrator, default)).Error!;

        Assert.Equal(["email", "role"], error.FieldErrors!.Keys.Order());
    }
}

public class StaffSignInTests
{
    private readonly ContentHarness _harness = new();
    private readonly CapturingOtpSender _sender = new();

    private StaffSignInService SignIn(params string[] bootstrap)
    {
        var hasher = new OnePondy.Infrastructure.Security.HmacSecretHasher(
            Options.Create(new OnePondy.Infrastructure.Security.SecretHashingOptions { HashingKey = TestKeys.Hashing }));
        var challenges = new OtpChallengeService(
            _harness.Db,
            _sender,
            hasher,
            Options.Create(new OtpOptions()),
            _harness.Time,
            NullLogger<OtpChallengeService>.Instance);
        var auth = new AuthService(
            _harness.Db,
            challenges,
            _harness.Accounts,
            new FakeAccessTokenIssuer(_harness.Time),
            hasher,
            Options.Create(new SessionOptions()),
            _harness.Time,
            NullLogger<AuthService>.Instance);
        return new StaffSignInService(
            _harness.Db,
            challenges,
            _harness.Accounts,
            _harness.Staff,
            auth,
            _harness.Audit,
            Options.Create(new ConsoleOptions { BootstrapAdministrators = bootstrap }),
            NullLogger<StaffSignInService>.Instance);
    }

    [Fact]
    public async Task Staff_get_a_code_and_a_session()
    {
        var signIn = SignIn();

        var requested = (await signIn.RequestCodeAsync("Editor@OnePondy.test", default)).Value!;
        var session = await signIn.VerifyCodeAsync(requested.ChallengeId, _sender.LastCodes["editor@onepondy.test"], "Chrome", default);

        Assert.True(session.IsSuccess);
        Assert.Contains(RoleNames.ContentEditor, session.Value.User.Roles);
    }

    [Fact]
    public async Task Everyone_else_gets_the_same_answer_but_no_code()
    {
        var signIn = SignIn();
        await _harness.Accounts.FindOrCreateAsync(OtpChannel.Email, "customer@onepondy.test", default);

        var customer = await signIn.RequestCodeAsync("customer@onepondy.test", default);
        var stranger = await signIn.RequestCodeAsync("nobody@onepondy.test", default);

        Assert.True(customer.IsSuccess && stranger.IsSuccess);
        Assert.Empty(_sender.LastCodes);
        Assert.Equal("invalid_code", (await signIn.VerifyCodeAsync(customer.Value!.ChallengeId, "000000", null, default)).Error!.Code);
    }

    [Fact]
    public async Task The_bootstrap_email_becomes_administrator_only_while_there_is_none()
    {
        // With an administrator present, the bootstrap list is ignored.
        await SignIn("founder@onepondy.test").RequestCodeAsync("founder@onepondy.test", default);
        Assert.Empty(_sender.LastCodes);

        _harness.Accounts.SetStaffRole(_harness.Administrator, null);
        _harness.Time.Advance(TimeSpan.FromMinutes(1));
        await SignIn("founder@onepondy.test").RequestCodeAsync("Founder@onepondy.test", default);
        Assert.True(_sender.LastCodes.ContainsKey("founder@onepondy.test"));
        Assert.Contains(_harness.Db.AuditEntries, e => e.Action == AuditActions.StaffRoleGranted && e.ActorUserId == null);
    }
}
