using System.Text.RegularExpressions;

namespace OnePondy.Domain.Notifications;

public enum DevicePlatform
{
    Android = 1,
    Ios = 2,
}

public enum NotificationStatus
{
    /// <summary>Expo accepted the message. That is not the same as the phone showing it.</summary>
    Sent = 1,

    /// <summary>Expo's receipt says the message reached the phone's notification service.</summary>
    Delivered = 2,

    Failed = 3,
}

/// <summary>Who a booking notification is addressed to. The recipient is checked again when it is sent.</summary>
public enum NotificationAudience
{
    Customer = 1,
    Partner = 2,
}

/// <summary>The kinds of push notification (spec Phase 7). Their text is generic by design: see <c>NotificationTemplates</c>.</summary>
public static class NotificationTypes
{
    public const string BookingRequested = "booking.requested";
    public const string BookingAccepted = "booking.accepted";
    public const string BookingConfirmed = "booking.confirmed";
    public const string BookingPaid = "booking.paid";
    public const string BookingRejected = "booking.rejected";
    public const string BookingCancelled = "booking.cancelled";
    public const string BookingExpired = "booking.expired";
    public const string BookingReminder = "booking.reminder";
    public const string RefundProcessed = "refund.processed";

    public static readonly IReadOnlyList<string> All =
    [
        BookingRequested,
        BookingAccepted,
        BookingConfirmed,
        BookingPaid,
        BookingRejected,
        BookingCancelled,
        BookingExpired,
        BookingReminder,
        RefundProcessed,
    ];
}

/// <summary>
/// A phone that can be sent push notifications for one signed-in session. The token is Expo's. It stops being used the
/// moment its session ends (sign-out, expiry, account deletion), and it is deleted when Expo says the phone no longer
/// has the app.
/// </summary>
public sealed partial class DeviceToken
{
    public const int MaxTokenLength = 200;

    /// <summary>The most phones one account keeps; registering another replaces the least recently seen.</summary>
    public const int MaxPerUser = 10;

    private DeviceToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The session that registered it. Notifications go only while this session is still active.</summary>
    public Guid SessionId { get; private set; }

    public string Token { get; private set; } = string.Empty;

    public DevicePlatform Platform { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>An Expo push token looks like <c>ExponentPushToken[...]</c> (older: <c>ExpoPushToken[...]</c>).</summary>
    public static bool IsValidToken(string? token) =>
        token is { Length: > 0 and <= MaxTokenLength } && TokenPattern().IsMatch(token);

    public static DeviceToken Register(Guid userId, Guid sessionId, string token, DevicePlatform platform, DateTimeOffset now)
    {
        if (!IsValidToken(token))
        {
            throw new ArgumentException("Not a valid Expo push token.", nameof(token));
        }

        return new DeviceToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SessionId = sessionId,
            Token = token,
            Platform = platform,
            CreatedAt = now,
            LastSeenAt = now,
        };
    }

    /// <summary>The same phone registers again, possibly for a different account (it changed hands) or session.</summary>
    public void Reassign(Guid userId, Guid sessionId, DevicePlatform platform, DateTimeOffset now)
    {
        UserId = userId;
        SessionId = sessionId;
        Platform = platform;
        LastSeenAt = now;
    }

    [GeneratedRegex(@"^(Exponent|Expo)PushToken\[[A-Za-z0-9_\-]{10,180}\]$")]
    private static partial Regex TokenPattern();
}

/// <summary>What was sent to whom, and what Expo said about it. Holds no message text, only the type, so it is safe to keep for support.</summary>
public sealed class NotificationLogEntry
{
    private NotificationLogEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid OutboxMessageId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Null once the token has been deleted (the phone signed out or lost the app).</summary>
    public Guid? DeviceTokenId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public NotificationStatus Status { get; private set; }

    /// <summary>Expo's id for the message, used to ask later whether it was delivered.</summary>
    public string? ProviderTicketId { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReceiptCheckedAt { get; private set; }

    public static NotificationLogEntry Accepted(Guid outboxMessageId, Guid userId, Guid deviceTokenId, string type, string ticketId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OutboxMessageId = outboxMessageId,
            UserId = userId,
            DeviceTokenId = deviceTokenId,
            Type = type,
            Status = NotificationStatus.Sent,
            ProviderTicketId = ticketId,
            CreatedAt = now,
        };

    public static NotificationLogEntry Refused(Guid outboxMessageId, Guid userId, Guid deviceTokenId, string type, string error, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OutboxMessageId = outboxMessageId,
            UserId = userId,
            DeviceTokenId = deviceTokenId,
            Type = type,
            Status = NotificationStatus.Failed,
            Error = Truncate(error),
            CreatedAt = now,
        };

    public void MarkDelivered(DateTimeOffset now)
    {
        Status = NotificationStatus.Delivered;
        ReceiptCheckedAt = now;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = NotificationStatus.Failed;
        Error = Truncate(error);
        ReceiptCheckedAt = now;
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200];
}
