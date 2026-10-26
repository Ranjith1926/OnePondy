using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Identity;

namespace OnePondy.Application.Staff;

public sealed class ConsoleOptions
{
    public const string SectionName = "Console";

    /// <summary>
    /// Emails that become administrators when no administrator exists yet, so a new environment can
    /// be set up. Once any administrator exists this list is ignored.
    /// </summary>
    public string[] BootstrapAdministrators { get; set; } = [];
}

/// <summary>
/// Web console sign-in with one-time codes by email (decided 2026-10-26). Codes are only sent to
/// staff, but every request gets the same response, so the form does not reveal who is staff.
/// </summary>
public sealed class StaffSignInService(
    IOnePondyDbContext db,
    OtpChallengeService challenges,
    IUserAccountStore accounts,
    IStaffDirectory staff,
    AuthService auth,
    AuditTrail audit,
    IOptions<ConsoleOptions> options,
    ILogger<StaffSignInService> logger)
{
    public async Task<Result<OtpRequested>> RequestCodeAsync(string? email, CancellationToken cancellationToken)
    {
        var normalized = challenges.NormalizeDestination(OtpChannel.Email, email);
        if (!normalized.IsSuccess)
        {
            return normalized.Error;
        }

        var destination = normalized.Value;
        await BootstrapAdministratorAsync(destination, cancellationToken);

        var account = await accounts.FindByContactAsync(OtpChannel.Email, destination, cancellationToken);
        var isStaff = account?.Roles.Any(RoleNames.IsStaff) == true;
        return await challenges.IssueAsync(OtpChannel.Email, OtpPurpose.StaffSignIn, destination, deliver: isStaff, cancellationToken);
    }

    public async Task<Result<SessionTokens>> VerifyCodeAsync(
        Guid challengeId,
        string? code,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        var verified = await challenges.VerifyAsync(challengeId, OtpPurpose.StaffSignIn, code, cancellationToken);
        if (!verified.IsSuccess)
        {
            return verified.Error;
        }

        var account = await accounts.FindByContactAsync(verified.Value.Channel, verified.Value.Destination, cancellationToken);
        if (account is null || !account.Roles.Any(RoleNames.IsStaff))
        {
            // Unreachable without a code, since codes only go to staff; answer as if the code were wrong.
            return AuthErrors.InvalidCode;
        }

        return await auth.StartSessionAsync(account, deviceName ?? "Web console", cancellationToken);
    }

    private async Task BootstrapAdministratorAsync(string email, CancellationToken cancellationToken)
    {
        var listed = options.Value.BootstrapAdministrators.Any(entry => ContactNormalizer.NormalizeEmail(entry) == email);
        if (!listed || await staff.CountWithRoleAsync(RoleNames.Administrator, cancellationToken) > 0)
        {
            return;
        }

        var account = await accounts.FindOrCreateAsync(OtpChannel.Email, email, cancellationToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await staff.SetRoleAsync(account.Id, RoleNames.Administrator, cancellationToken);
        audit.Record(null, AuditActions.StaffRoleGranted, AuditEntityTypes.Staff, account.Id, new
        {
            role = RoleNames.Administrator,
            reason = "bootstrap",
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogWarning("Bootstrap administrator {UserId} created from Console:BootstrapAdministrators", account.Id);
    }
}
