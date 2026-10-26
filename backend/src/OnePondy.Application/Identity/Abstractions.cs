using OnePondy.Domain.Identity;

namespace OnePondy.Application.Identity;

public sealed record UserAccount(Guid Id, string DisplayName, IReadOnlyList<string> Roles, bool IsNew = false);

public sealed record AccountProfile(
    Guid Id,
    string DisplayName,
    string? PhoneNumber,
    string? Email,
    string PreferredLanguage,
    DateTimeOffset CreatedAt);

public sealed record AccountContact(OtpChannel Channel, string Destination);

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>User accounts, backed by ASP.NET Core Identity in Infrastructure.</summary>
public interface IUserAccountStore
{
    /// <summary>Signs in an existing account or creates one; sign-up and sign-in share one flow.</summary>
    Task<UserAccount> FindOrCreateAsync(OtpChannel channel, string destination, CancellationToken cancellationToken);

    Task<UserAccount?> FindByContactAsync(OtpChannel channel, string destination, CancellationToken cancellationToken);

    /// <summary>Returns the account unless it has been deleted or deactivated.</summary>
    Task<UserAccount?> FindActiveAsync(Guid userId, CancellationToken cancellationToken);

    Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    Task<AccountProfile?> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? preferredLanguage,
        CancellationToken cancellationToken);

    /// <summary>The verified contact used for codes: the phone number if there is one, otherwise the email.</summary>
    Task<AccountContact?> GetContactAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes personal data and marks the account deleted. The row stays so records that must be
    /// kept by law can still reference it (docs/data-retention.md).
    /// </summary>
    Task AnonymizeAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IAccessTokenIssuer
{
    AccessToken Issue(Guid userId, IReadOnlyCollection<string> roles, Guid sessionId);
}

/// <summary>Delivers one-time codes and account notices by SMS or email.</summary>
public interface IOtpSender
{
    Task SendCodeAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken);

    Task SendAccountDeletedAsync(OtpChannel channel, string destination, CancellationToken cancellationToken);
}

/// <summary>Thrown by an <see cref="IOtpSender"/> when a message cannot be delivered.</summary>
public sealed class OtpDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Reasons an account cannot be deleted yet, such as bookings or refunds in progress.</summary>
public interface IAccountDeletionGuard
{
    Task<IReadOnlyList<string>> GetBlockersAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// No bookings, orders or refunds exist before Phases 6–8, so nothing blocks deletion yet. Those
/// modules will contribute their own checks.
/// </summary>
public sealed class NoCommitmentsDeletionGuard : IAccountDeletionGuard
{
    public Task<IReadOnlyList<string>> GetBlockersAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
