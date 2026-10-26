using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Identity;

namespace OnePondy.Application.Staff;

public sealed record StaffMember(Guid UserId, string DisplayName, string? Email, string Role);

/// <summary>Staff accounts and roles, backed by ASP.NET Core Identity in Infrastructure.</summary>
public interface IStaffDirectory
{
    Task<IReadOnlyList<StaffMember>> ListAsync(CancellationToken cancellationToken);

    Task<StaffMember?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Replaces the user's staff role; null removes console access.</summary>
    Task SetRoleAsync(Guid userId, string? role, CancellationToken cancellationToken);

    Task<int> CountWithRoleAsync(string role, CancellationToken cancellationToken);

    /// <summary>Names to show in the console: the display name, else the email, else "Deleted user".</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

public static class StaffErrors
{
    public static readonly AppError NotFound = new("staff_not_found", ErrorKind.NotFound, "This person is not on the staff list.");

    public static readonly AppError OwnAccess = new(
        "own_access",
        ErrorKind.Conflict,
        "You cannot change or remove your own administrator access. Ask another administrator.");

    public static readonly AppError LastAdministrator = new(
        "last_administrator",
        ErrorKind.Conflict,
        "There must always be at least one administrator.");
}

/// <summary>
/// Gives people console access by email (spec §10, Phase 3). Changing or removing someone's role
/// signs them out everywhere, so the change applies at once rather than when their token expires.
/// </summary>
public sealed class StaffService(
    IOnePondyDbContext db,
    IStaffDirectory directory,
    IUserAccountStore accounts,
    AuditTrail audit,
    TimeProvider time)
{
    public Task<IReadOnlyList<StaffMember>> ListAsync(CancellationToken cancellationToken) =>
        directory.ListAsync(cancellationToken);

    public async Task<Result<StaffMember>> AddAsync(string? email, string? role, Guid actorId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var normalized = ContactNormalizer.NormalizeEmail(email);
        if (normalized is null)
        {
            errors.Add("email", "Enter a valid email address.");
        }

        if (role is null || !RoleNames.IsStaff(role))
        {
            errors.Add("role", "Choose a role.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var account = await accounts.FindOrCreateAsync(OtpChannel.Email, normalized!, cancellationToken);
        return await ApplyRoleAsync(account.Id, role!, actorId, cancellationToken);
    }

    public async Task<Result<StaffMember>> ChangeRoleAsync(Guid userId, string? role, Guid actorId, CancellationToken cancellationToken)
    {
        if (role is null || !RoleNames.IsStaff(role))
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["role"] = ["Choose a role."] });
        }

        var member = await directory.FindAsync(userId, cancellationToken);
        if (member is null)
        {
            return StaffErrors.NotFound;
        }

        var guard = await GuardAdministratorAsync(member, role, actorId, cancellationToken);
        return guard ?? await ApplyRoleAsync(userId, role, actorId, cancellationToken);
    }

    public async Task<AppError?> RemoveAsync(Guid userId, Guid actorId, CancellationToken cancellationToken)
    {
        var member = await directory.FindAsync(userId, cancellationToken);
        if (member is null)
        {
            return StaffErrors.NotFound;
        }

        var guard = await GuardAdministratorAsync(member, newRole: null, actorId, cancellationToken);
        if (guard is not null)
        {
            return guard;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await directory.SetRoleAsync(userId, null, cancellationToken);
        audit.Record(actorId, AuditActions.StaffRoleRevoked, AuditEntityTypes.Staff, userId, new { role = member.Role });
        await SignOutEverywhereAsync(userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private async Task<Result<StaffMember>> ApplyRoleAsync(Guid userId, string role, Guid actorId, CancellationToken cancellationToken)
    {
        var before = await directory.FindAsync(userId, cancellationToken);
        if (before?.Role == role)
        {
            return before;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await directory.SetRoleAsync(userId, role, cancellationToken);
        audit.Record(actorId, AuditActions.StaffRoleGranted, AuditEntityTypes.Staff, userId, new { role, previousRole = before?.Role });
        await SignOutEverywhereAsync(userId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await directory.FindAsync(userId, cancellationToken))!;
    }

    private async Task<AppError?> GuardAdministratorAsync(
        StaffMember member,
        string? newRole,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (member.Role != RoleNames.Administrator || newRole == RoleNames.Administrator)
        {
            return null;
        }

        if (member.UserId == actorId)
        {
            return StaffErrors.OwnAccess;
        }

        return await directory.CountWithRoleAsync(RoleNames.Administrator, cancellationToken) <= 1
            ? StaffErrors.LastAdministrator
            : null;
    }

    /// <summary>Also saves the pending audit entry.</summary>
    private async Task SignOutEverywhereAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var sessions = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(SessionRevocationReason.RoleChanged, now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
