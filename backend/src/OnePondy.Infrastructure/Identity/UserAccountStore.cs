using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Persistence;

namespace OnePondy.Infrastructure.Identity;

public sealed class UserAccountStore(
    UserManager<ApplicationUser> userManager,
    OnePondyDbContext db,
    TimeProvider time) : IUserAccountStore
{
    public async Task<UserAccount> FindOrCreateAsync(
        OtpChannel channel,
        string destination,
        CancellationToken cancellationToken)
    {
        if (await FindUserAsync(channel, destination, cancellationToken) is { } existing)
        {
            return await ToAccountAsync(existing);
        }

        var now = time.GetUtcNow();
        var user = new ApplicationUser { Id = Guid.CreateVersion7(), CreatedAt = now, UpdatedAt = now };
        user.UserName = user.Id.ToString("N");
        if (channel == OtpChannel.Sms)
        {
            user.PhoneNumber = destination;
            user.PhoneNumberConfirmed = true;
        }
        else
        {
            user.Email = destination;
            user.EmailConfirmed = true;
        }

        try
        {
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create account: " + string.Join(", ", created.Errors.Select(e => e.Code)));
            }
        }
        catch (DbUpdateException)
        {
            // A concurrent sign-in created the same account first (unique phone/email index); use it.
            db.ChangeTracker.Clear();
            if (await FindUserAsync(channel, destination, cancellationToken) is { } raced)
            {
                return await ToAccountAsync(raced);
            }

            throw;
        }

        await userManager.AddToRoleAsync(user, RoleNames.Customer);
        return new UserAccount(user.Id, user.DisplayName, [RoleNames.Customer], IsNew: true);
    }

    public async Task<UserAccount?> FindByContactAsync(
        OtpChannel channel,
        string destination,
        CancellationToken cancellationToken) =>
        await FindUserAsync(channel, destination, cancellationToken) is { } user ? await ToAccountAsync(user) : null;

    public async Task<UserAccount?> FindActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(
            u => u.Id == userId && u.DeletedAt == null && u.IsActive,
            cancellationToken);
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            u => u.Id == userId && u.DeletedAt == null,
            cancellationToken);
        return user is null ? null : ToProfile(user);
    }

    public async Task<AccountProfile?> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? preferredLanguage,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.DisplayName = displayName ?? user.DisplayName;
        user.PreferredLanguage = preferredLanguage ?? user.PreferredLanguage;
        user.UpdatedAt = time.GetUtcNow();
        await userManager.UpdateAsync(user);
        return ToProfile(user);
    }

    public async Task<AccountContact?> GetContactAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            u => u.Id == userId && u.DeletedAt == null,
            cancellationToken);
        return user switch
        {
            { PhoneNumber: { } phone } => new AccountContact(OtpChannel.Sms, phone),
            { Email: { } email } => new AccountContact(OtpChannel.Email, email),
            _ => null,
        };
    }

    public async Task AnonymizeAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        user.DeletedAt = now;
        user.IsActive = false;
        user.DisplayName = "Deleted user";
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.Email = null;
        user.NormalizedEmail = null;
        user.EmailConfirmed = false;
        user.UpdatedAt = now;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await userManager.UpdateAsync(user);
    }

    private Task<ApplicationUser?> FindUserAsync(OtpChannel channel, string destination, CancellationToken cancellationToken)
    {
        if (channel == OtpChannel.Sms)
        {
            return db.Users.SingleOrDefaultAsync(u => u.PhoneNumber == destination && u.DeletedAt == null, cancellationToken);
        }

        var normalized = userManager.NormalizeEmail(destination);
        return db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized && u.DeletedAt == null, cancellationToken);
    }

    private async Task<UserAccount> ToAccountAsync(ApplicationUser user) =>
        new(user.Id, user.DisplayName, [.. await userManager.GetRolesAsync(user)]);

    private static AccountProfile ToProfile(ApplicationUser user) =>
        new(user.Id, user.DisplayName, user.PhoneNumber, user.Email, user.PreferredLanguage, user.CreatedAt);
}
