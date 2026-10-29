using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Domain.Notifications;

namespace OnePondy.Application.Notifications;

public sealed record DeviceView(Guid Id);

/// <summary>
/// Registering a phone for push notifications. A token belongs to one signed-in session: when that session ends the
/// phone stops receiving notifications, with nothing for the app to remember to do (the Worker checks the session each time).
/// </summary>
public sealed class DeviceService(IOnePondyDbContext db, TimeProvider time)
{
    /// <summary>
    /// Registers the phone, or refreshes it if it is already known. The same phone registering again, even for another account
    /// (it changed hands), moves to the new owner, so the previous owner stops receiving its notifications.
    /// </summary>
    public async Task<Result<DeviceView>> RegisterAsync(Guid userId, Guid sessionId, string? token, string? platform, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        if (!DeviceToken.IsValidToken(token?.Trim()))
        {
            errors.Add("token", "Enter a valid Expo push token.");
        }

        if (!Enum.TryParse<DevicePlatform>(platform?.Trim(), ignoreCase: true, out var parsedPlatform) || !Enum.IsDefined(parsedPlatform))
        {
            errors.Add("platform", "Choose android or ios.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var trimmed = token!.Trim();
        var now = time.GetUtcNow();
        for (var attempt = 0; ; attempt++)
        {
            var device = await db.DeviceTokens.SingleOrDefaultAsync(d => d.Token == trimmed, cancellationToken);
            if (device is null)
            {
                device = DeviceToken.Register(userId, sessionId, trimmed, parsedPlatform, now);
                db.DeviceTokens.Add(device);
            }
            else
            {
                device.Reassign(userId, sessionId, parsedPlatform, now);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (attempt == 0 && WriteConflicts.Is(ex))
            {
                // The same token was registered on another request at the same moment: look again and refresh that one.
                db.ClearTracked();
                continue;
            }

            await TrimOldDevicesAsync(userId, cancellationToken);
            return new DeviceView(device.Id);
        }
    }

    /// <summary>Forgets one of the caller's phones, for example when they turn notifications off. Someone else's device is "not found".</summary>
    public async Task<AppError?> UnregisterAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await db.DeviceTokens.SingleOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId, cancellationToken);
        if (device is null)
        {
            return NotificationErrors.DeviceNotFound;
        }

        db.DeviceTokens.Remove(device);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task TrimOldDevicesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var devices = await db.DeviceTokens.Where(d => d.UserId == userId).OrderByDescending(d => d.LastSeenAt).ToListAsync(cancellationToken);
        if (devices.Count <= DeviceToken.MaxPerUser)
        {
            return;
        }

        db.DeviceTokens.RemoveRange(devices.Skip(DeviceToken.MaxPerUser));
        await db.SaveChangesAsync(cancellationToken);
    }
}
