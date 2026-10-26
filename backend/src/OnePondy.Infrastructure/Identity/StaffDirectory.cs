using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Staff;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.Infrastructure.Identity;

/// <summary>Staff roles on top of ASP.NET Core Identity. A person holds at most one staff role.</summary>
public sealed class StaffDirectory(UserManager<ApplicationUser> userManager, OnePondyDbContext db) : IStaffDirectory
{
    public async Task<IReadOnlyList<StaffMember>> ListAsync(CancellationToken cancellationToken) =>
        await QueryAsync(userId: null, cancellationToken);

    public async Task<StaffMember?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        (await QueryAsync(userId, cancellationToken)).SingleOrDefault();

    public async Task SetRoleAsync(Guid userId, string? role, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} does not exist.");
        var current = (await userManager.GetRolesAsync(user)).Where(RoleNames.IsStaff).ToList();
        if (current.Count > 0)
        {
            Check(await userManager.RemoveFromRolesAsync(user, current));
        }

        if (role is not null)
        {
            Check(await userManager.AddToRoleAsync(user, role));
        }
    }

    public async Task<int> CountWithRoleAsync(string role, CancellationToken cancellationToken) =>
        (await userManager.GetUsersInRoleAsync(role)).Count(u => u.DeletedAt == null && u.IsActive);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var users = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(cancellationToken);
        return users.ToDictionary(u => u.Id, u => Name(u.DisplayName, u.Email));
    }

    private async Task<List<StaffMember>> QueryAsync(Guid? userId, CancellationToken cancellationToken)
    {
        var rows = await (
                from link in db.UserRoles
                join role in db.Roles on link.RoleId equals role.Id
                join user in db.Users on link.UserId equals user.Id
                where RoleNames.Staff.Contains(role.Name!) && user.DeletedAt == null
                    && (userId == null || user.Id == userId)
                select new { user.Id, user.DisplayName, user.Email, Role = role.Name! })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // If someone ever holds two staff roles, show the most powerful one.
        return
        [
            .. rows.GroupBy(r => r.Id)
                .Select(g => g.OrderBy(r => RoleRank(r.Role)).First())
                .Select(r => new StaffMember(r.Id, Name(r.DisplayName, r.Email), r.Email, r.Role))
                .OrderBy(m => RoleRank(m.Role))
                .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static int RoleRank(string role) => role switch
    {
        RoleNames.Administrator => 0,
        RoleNames.ContentEditor => 1,
        _ => 2,
    };

    private static string Name(string displayName, string? email) =>
        !string.IsNullOrWhiteSpace(displayName) ? displayName : email ?? "Deleted user";

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Role change failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }
}
