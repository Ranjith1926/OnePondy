using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OnePondy.Application.Common;
using OnePondy.Domain.Identity;

namespace OnePondy.Application.Identity;

/// <summary>Profile management and account deletion (spec §7, account deletion workflow).</summary>
public sealed class AccountService(
    IOnePondyDbContext db,
    OtpChallengeService challenges,
    IUserAccountStore accounts,
    IAccountDeletionGuard deletionGuard,
    IOtpSender sender,
    ISecretHasher hasher,
    TimeProvider time,
    ILogger<AccountService> logger)
{
    private static readonly string[] SupportedLanguages = ["en", "ta", "fr"];

    public async Task<Result<AccountProfile>> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        await accounts.GetProfileAsync(userId, cancellationToken) is { } profile
            ? profile
            : AuthErrors.AccountNotFound;

    public async Task<Result<AccountProfile>> UpdateProfileAsync(
        Guid userId,
        string? displayName,
        string? preferredLanguage,
        CancellationToken cancellationToken)
    {
        var name = displayName?.Trim();
        if (name is not null && (name.Length is 0 or > 120 || name.Any(char.IsControl)))
        {
            return AuthErrors.InvalidProfile("Enter a name between 1 and 120 characters.");
        }

        if (preferredLanguage is not null && !SupportedLanguages.Contains(preferredLanguage))
        {
            return AuthErrors.InvalidProfile("The language is not supported.");
        }

        return await accounts.UpdateProfileAsync(userId, name, preferredLanguage, cancellationToken) is { } profile
            ? profile
            : AuthErrors.AccountNotFound;
    }

    /// <summary>
    /// Sends a code to the account's own contact; deleting requires proof of access to it. A signed-in
    /// user is told up front what must finish first, so no code is wasted on a deletion that cannot
    /// go ahead. (The public form cannot do this: it must not reveal whether an account exists.)
    /// </summary>
    public async Task<Result<OtpRequested>> RequestDeletionCodeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var contact = await accounts.GetContactAsync(userId, cancellationToken);
        if (contact is null)
        {
            return AuthErrors.AccountNotFound;
        }

        var blockers = await deletionGuard.GetBlockersAsync(userId, cancellationToken);
        if (blockers.Count > 0)
        {
            return AuthErrors.DeletionBlocked(blockers);
        }

        return await challenges.IssueAsync(
            contact.Channel,
            OtpPurpose.AccountDeletion,
            contact.Destination,
            deliver: true,
            cancellationToken);
    }

    /// <returns>Null when the account was deleted, otherwise the reason it was not.</returns>
    public async Task<AppError?> DeleteAccountAsync(
        Guid userId,
        Guid challengeId,
        string? code,
        CancellationToken cancellationToken)
    {
        var contact = await accounts.GetContactAsync(userId, cancellationToken);
        if (contact is null)
        {
            return AuthErrors.AccountNotFound;
        }

        var verified = await challenges.VerifyAsync(challengeId, OtpPurpose.AccountDeletion, code, cancellationToken);
        if (!verified.IsSuccess)
        {
            return verified.Error;
        }

        if (verified.Value.Destination != contact.Destination)
        {
            return AuthErrors.InvalidCode;
        }

        return await DeleteVerifiedAsync(userId, contact, cancellationToken);
    }

    /// <summary>
    /// Public web flow (required by Google Play): a code is recorded for any destination but only
    /// sent when an account exists, so the response never reveals whether one does.
    /// </summary>
    public async Task<Result<OtpRequested>> RequestDeletionCodeForContactAsync(
        OtpChannel channel,
        string? destination,
        CancellationToken cancellationToken)
    {
        var normalized = challenges.NormalizeDestination(channel, destination);
        if (!normalized.IsSuccess)
        {
            return normalized.Error;
        }

        var account = await accounts.FindByContactAsync(channel, normalized.Value, cancellationToken);
        return await challenges.IssueAsync(
            channel,
            OtpPurpose.AccountDeletion,
            normalized.Value,
            deliver: account is not null,
            cancellationToken);
    }

    /// <returns>Null when no account remains for the contact, otherwise the reason deletion failed.</returns>
    public async Task<AppError?> ConfirmDeletionForContactAsync(
        Guid challengeId,
        string? code,
        CancellationToken cancellationToken)
    {
        var verified = await challenges.VerifyAsync(challengeId, OtpPurpose.AccountDeletion, code, cancellationToken);
        if (!verified.IsSuccess)
        {
            return verified.Error;
        }

        var contact = new AccountContact(verified.Value.Channel, verified.Value.Destination);
        var account = await accounts.FindByContactAsync(contact.Channel, contact.Destination, cancellationToken);
        return account is null ? null : await DeleteVerifiedAsync(account.Id, contact, cancellationToken);
    }

    private async Task<AppError?> DeleteVerifiedAsync(
        Guid userId,
        AccountContact contact,
        CancellationToken cancellationToken)
    {
        var blockers = await deletionGuard.GetBlockersAsync(userId, cancellationToken);
        if (blockers.Count > 0)
        {
            return AuthErrors.DeletionBlocked(blockers);
        }

        var now = time.GetUtcNow();
        var sessions = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(SessionRevocationReason.AccountDeleted, now);
        }

        // Account rows are anonymised, never hard-deleted, so the saved_places/itineraries foreign
        // keys never cascade; deleting them explicitly is what spec §17.3 means by "account deletion
        // deletes saved places ... and itineraries". Loaded and removed rather than ExecuteDelete, so
        // this works the same against every provider, including the in-memory one tests use.
        db.SavedPlaces.RemoveRange(await db.SavedPlaces.Where(s => s.UserId == userId).ToListAsync(cancellationToken));
        db.SavedListings.RemoveRange(await db.SavedListings.Where(s => s.UserId == userId).ToListAsync(cancellationToken));
        db.Itineraries.RemoveRange(await db.Itineraries.Where(i => i.UserId == userId).ToListAsync(cancellationToken));

        await db.SaveChangesAsync(cancellationToken);
        await accounts.AnonymizeAsync(userId, now, cancellationToken);
        logger.LogInformation("Account {UserId} deleted; {SessionCount} sessions revoked", userId, sessions.Count);

        try
        {
            await sender.SendAccountDeletedAsync(contact.Channel, contact.Destination, cancellationToken);
        }
        catch (OtpDeliveryException exception)
        {
            // The deletion stands even if the confirmation message cannot be delivered.
            logger.LogWarning(
                exception,
                "Deletion confirmation not delivered to {DestinationFingerprint}",
                hasher.Fingerprint(contact.Destination));
        }

        return null;
    }
}
