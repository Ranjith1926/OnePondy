using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Notifications;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Notifications;

/// <summary>Settings for push notifications (spec D7: Expo push service). Off until a deployment turns it on.</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// Whether the Worker sends push notifications. Off by default, so nothing is sent until Expo is set up. Messages are still
    /// recorded either way, but any older than <see cref="MessageLifetime"/> when sending starts are dropped, not delivered late.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>A notification is worthless once it is stale ("your booking is tomorrow" the day after), so older ones are dropped.</summary>
    public TimeSpan MessageLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Expo asks that receipts be checked about 15 minutes after sending.</summary>
    public TimeSpan ReceiptDelay { get; set; } = TimeSpan.FromMinutes(15);

    public ExpoOptions Expo { get; set; } = new();
}

public sealed class ExpoOptions
{
    /// <summary>Only needed if enhanced push security is switched on for the Expo project. Set it as a secret, never in a committed file.</summary>
    public string? AccessToken { get; set; }

    public string BaseUrl { get; set; } = "https://exp.host";
}

/// <param name="To">An Expo push token.</param>
/// <param name="Type">One of <see cref="NotificationTypes"/>; the app uses it to decide where tapping the notification goes.</param>
/// <param name="BookingId">The only thing the notification carries about a booking. The app loads details through the normal, authorized API.</param>
public sealed record PushMessage(string To, string Title, string Body, string Type, Guid BookingId);

/// <param name="Id">Expo's id for the message, when it accepted it.</param>
/// <param name="Error">Expo's reason when it did not, for example <c>DeviceNotRegistered</c>.</param>
public sealed record PushTicket(string? Id, string? Error);

public sealed record PushReceipt(bool Delivered, string? Error);

/// <summary>Sends push notifications (spec D7). Implementations never log message text or tokens.</summary>
public interface IPushSender
{
    /// <summary>False when no provider is set up, so the dispatcher leaves messages alone instead of failing them.</summary>
    bool IsAvailable { get; }

    /// <summary>Tickets come back in the order of the messages. A failure means the request itself did not get through.</summary>
    Task<Result<IReadOnlyList<PushTicket>>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken);

    /// <summary>Receipts for earlier tickets. An id with no receipt yet is simply absent from the result.</summary>
    Task<Result<IReadOnlyDictionary<string, PushReceipt>>> GetReceiptsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken);
}

public static class NotificationErrors
{
    public static readonly AppError Unavailable = new(
        "notifications_unavailable",
        ErrorKind.Unavailable,
        "Push notifications are not available right now.");

    public static readonly AppError DeviceNotFound = new(
        "device_not_found",
        ErrorKind.NotFound,
        "That device is not registered.");
}

/// <summary>
/// The words of each notification. Deliberately generic: no name, amount, listing, date or place, because a notification
/// shows on a locked screen and can reach a phone whose owner has changed. The app opens the booking, and the booking is
/// then loaded through the normal sign-in-protected API (spec Phase 7: "notifications do not expose private booking
/// details to unauthorized recipients").
/// </summary>
public static class NotificationTemplates
{
    public const string Title = "OnePondy";

    /// <summary>English only for now: the app has no other language yet. Another language is added here and falls back to English until then.</summary>
    public static (string Title, string Body) For(string type, string? language)
    {
        _ = language;
        var body = type switch
        {
            NotificationTypes.BookingRequested => "You have a new booking request.",
            NotificationTypes.BookingAccepted => "Your booking request was accepted. Open the app to pay and confirm it.",
            NotificationTypes.BookingConfirmed => "Your booking is confirmed.",
            NotificationTypes.BookingPaid => "A booking was paid and confirmed.",
            NotificationTypes.BookingRejected => "Your booking request was declined.",
            NotificationTypes.BookingCancelled => "A booking was cancelled.",
            NotificationTypes.BookingExpired => "Your booking request expired.",
            NotificationTypes.BookingReminder => "You have a booking tomorrow.",
            NotificationTypes.RefundProcessed => "Your refund has been sent.",
            _ => "You have an update on a booking.",
        };
        return (Title, body);
    }
}

/// <summary>What an outbox message says about who it is for and what it is about.</summary>
public sealed record NotificationPayload(Guid RecipientUserId, NotificationAudience Audience, Guid BookingId);

/// <summary>
/// Records notifications in the same save as the change that causes them (the transactional outbox, spec §10), so a booking
/// changing and its notification either both happen or neither does. The Worker sends them afterwards. Nothing here talks to
/// the network.
/// </summary>
public sealed class NotificationOutbox(IOnePondyDbContext db, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public static NotificationPayload? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NotificationPayload>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Enqueue(string type, Guid recipientUserId, NotificationAudience audience, Guid bookingId, string? dedupeKey = null) =>
        db.OutboxMessages.Add(OutboxMessage.Create(
            type,
            JsonSerializer.Serialize(new NotificationPayload(recipientUserId, audience, bookingId), Json),
            time.GetUtcNow(),
            dedupeKey));

    /// <summary>A customer asked for a booking: tell the business's owner.</summary>
    public async Task EnqueueRequestedAsync(Booking booking, CancellationToken cancellationToken) =>
        Enqueue(NotificationTypes.BookingRequested, await OwnerOfAsync(booking, cancellationToken), NotificationAudience.Partner, booking.Id);

    /// <summary>A booking moved from one status to another by someone: tell whoever did not do it, when it matters to them.</summary>
    public async Task EnqueueTransitionAsync(Booking booking, BookingStatus? previous, BookingStatus next, BookingActorType actor, CancellationToken cancellationToken)
    {
        switch (previous, next)
        {
            case (BookingStatus.Pending, BookingStatus.AwaitingPayment):
                Enqueue(NotificationTypes.BookingAccepted, booking.CustomerUserId, NotificationAudience.Customer, booking.Id);
                break;
            case (BookingStatus.Pending, BookingStatus.Confirmed) when actor == BookingActorType.Partner:
                Enqueue(NotificationTypes.BookingConfirmed, booking.CustomerUserId, NotificationAudience.Customer, booking.Id);
                break;
            case (_, BookingStatus.Rejected):
                Enqueue(NotificationTypes.BookingRejected, booking.CustomerUserId, NotificationAudience.Customer, booking.Id);
                break;
            case (_, BookingStatus.Cancelled) when actor == BookingActorType.Customer:
                Enqueue(NotificationTypes.BookingCancelled, await OwnerOfAsync(booking, cancellationToken), NotificationAudience.Partner, booking.Id);
                break;
            case (_, BookingStatus.Cancelled):
                Enqueue(NotificationTypes.BookingCancelled, booking.CustomerUserId, NotificationAudience.Customer, booking.Id);
                break;
            case (_, BookingStatus.Expired):
                Enqueue(NotificationTypes.BookingExpired, booking.CustomerUserId, NotificationAudience.Customer, booking.Id);
                break;
        }
    }

    /// <summary>A verified payment confirmed a booking: tell the customer it is done and the partner it is paid.</summary>
    public async Task EnqueuePaymentConfirmedAsync(Booking booking, CancellationToken cancellationToken)
    {
        Enqueue(NotificationTypes.BookingConfirmed, booking.CustomerUserId, NotificationAudience.Customer, booking.Id);
        Enqueue(NotificationTypes.BookingPaid, await OwnerOfAsync(booking, cancellationToken), NotificationAudience.Partner, booking.Id);
    }

    public void EnqueueRefundProcessed(Booking booking) =>
        Enqueue(NotificationTypes.RefundProcessed, booking.CustomerUserId, NotificationAudience.Customer, booking.Id);

    private Task<Guid> OwnerOfAsync(Booking booking, CancellationToken cancellationToken) =>
        db.Businesses.AsNoTracking().Where(b => b.Id == booking.BusinessId).Select(b => b.OwnerUserId).SingleAsync(cancellationToken);
}
