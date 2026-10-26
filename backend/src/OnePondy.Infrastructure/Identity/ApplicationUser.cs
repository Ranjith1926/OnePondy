using Microsoft.AspNetCore.Identity;

namespace OnePondy.Infrastructure.Identity;

/// <summary>
/// Maps to <c>app_users</c> (spec §17.3). Sign-in is passwordless, so the password columns stay empty;
/// the user name is the account id so it never contains personal data.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public string PreferredLanguage { get; set; } = "en";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
