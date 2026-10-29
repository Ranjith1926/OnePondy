using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Notifications;
using OnePondy.Domain.Payments;

namespace OnePondy.Application.Notifications;

/// <param name="Sent">Messages delivered to Expo for at least one phone.</param>
/// <param name="Discarded">Messages finished without sending: no phone, the recipient is no longer entitled, or too old.</param>
/// <param name="Retrying">Messages that failed and will be tried again.</param>
public sealed record DispatchResult(int Sent, int Discarded, int Retrying);

/// <summary>
/// Sends the notifications the outbox holds (spec Phase 7: "push notifications ... sent through the outbox"). For each message
/// it checks again that the recipient is still entitled to hear about the booking, sends only to phones whose session is still
/// signed in, and records what Expo said. Retries failures with a back-off, and gives up after <see cref="OutboxMessage.MaxAttempts"/>.
/// </summary>
public sealed class OutboxDispatcher(
    IOnePondyDbContext db,
    IPushSender sender,
    IUserAccountStore accounts,
    TimeProvider time,
    IOptions<NotificationOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    private const string DeviceNotRegistered = "DeviceNotRegistered";

    /// <summary>One dispatcher holds a message this long while it sends, so another does not send it too. If it dies, the message is tried again.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private readonly NotificationOptions _options = options.Value;

    public async Task<DispatchResult> DispatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !sender.IsAvailable)
        {
            return new DispatchResult(0, 0, 0);
        }

        var now = time.GetUtcNow();
        var due = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.DispatchedAt == null && m.Attempts < OutboxMessage.MaxAttempts && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        int sent = 0, discarded = 0, retrying = 0;
        foreach (var id in due)
        {
            var outcome = await DispatchOneAsync(id, cancellationToken);
            switch (outcome)
            {
                case Outcome.Sent:
                    sent++;
                    break;
                case Outcome.Discarded:
                    discarded++;
                    break;
                case Outcome.Retrying:
                    retrying++;
                    break;
            }
        }

        return new DispatchResult(sent, discarded, retrying);
    }

    private enum Outcome
    {
        Skipped,
        Sent,
        Discarded,
        Retrying,
    }

    private async Task<Outcome> DispatchOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var message = await db.OutboxMessages.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (message is null || !message.IsPending || message.NextAttemptAt > now)
        {
            return Outcome.Skipped;
        }

        if (now - message.CreatedAt > _options.MessageLifetime)
        {
            message.Discard("too old to be useful", now);
            await db.SaveChangesAsync(cancellationToken);
            return Outcome.Discarded;
        }

        // Take it. Two dispatchers cannot both: the row version makes the second save fail.
        message.Lease(now + LeaseDuration);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ClearTracked();
            return Outcome.Skipped;
        }

        return await DeliverAsync(message, now, cancellationToken);
    }

    private async Task<Outcome> DeliverAsync(OutboxMessage message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var payload = NotificationOutbox.Parse(message.Payload);
        if (payload is null)
        {
            return await DiscardAsync(message, "the message could not be read", now, cancellationToken);
        }

        // The recipient is checked again now, not when the message was written: a notification is never sent to someone
        // who is not the booking's customer or the business's owner, nor to a closed account.
        var booking = await db.Bookings.AsNoTracking()
            .Where(b => b.Id == payload.BookingId)
            .Select(b => new { b.CustomerUserId, b.BusinessId, b.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (booking is null || !await IsStillEntitledAsync(payload, booking.CustomerUserId, booking.BusinessId, cancellationToken))
        {
            logger.LogWarning("A {Type} notification was dropped because its recipient is no longer entitled to it", message.Type);
            return await DiscardAsync(message, "the recipient is not entitled to this booking", now, cancellationToken);
        }

        // Some messages say something that stops being true: a reminder for a booking that was cancelled, or "pay now" once it is paid.
        if ((message.Type == NotificationTypes.BookingReminder && booking.Status != BookingStatus.Confirmed)
            || (message.Type == NotificationTypes.BookingAccepted && booking.Status != BookingStatus.AwaitingPayment))
        {
            return await DiscardAsync(message, "no longer true of the booking", now, cancellationToken);
        }

        if (await accounts.FindActiveAsync(payload.RecipientUserId, cancellationToken) is null)
        {
            return await DiscardAsync(message, "the recipient's account is closed", now, cancellationToken);
        }

        var devices = await db.DeviceTokens
            .Where(d => d.UserId == payload.RecipientUserId
                && db.UserSessions.Any(s => s.Id == d.SessionId && s.RevokedAt == null && s.ExpiresAt > now))
            .ToListAsync(cancellationToken);
        if (devices.Count == 0)
        {
            return await DiscardAsync(message, "no signed-in phone to send to", now, cancellationToken);
        }

        var language = (await accounts.GetProfileAsync(payload.RecipientUserId, cancellationToken))?.PreferredLanguage;
        var (title, body) = NotificationTemplates.For(message.Type, language);
        var pushes = devices.Select(d => new PushMessage(d.Token, title, body, message.Type, payload.BookingId)).ToList();

        var tickets = await sender.SendAsync(pushes, cancellationToken);
        if (!tickets.IsSuccess || tickets.Value.Count != pushes.Count)
        {
            return await RetryLaterAsync(message, tickets.IsSuccess ? "unexpected reply" : tickets.Error.Code, now, cancellationToken);
        }

        var accepted = 0;
        var retryable = 0;
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            var ticket = tickets.Value[i];
            if (ticket.Error is null && ticket.Id is not null)
            {
                accepted++;
                db.NotificationLog.Add(NotificationLogEntry.Accepted(message.Id, payload.RecipientUserId, device.Id, message.Type, ticket.Id, now));
                continue;
            }

            db.NotificationLog.Add(NotificationLogEntry.Refused(message.Id, payload.RecipientUserId, device.Id, message.Type, ticket.Error ?? "no ticket", now));
            if (ticket.Error == DeviceNotRegistered)
            {
                // The phone no longer has the app: stop sending to it. It registers again if it comes back.
                db.DeviceTokens.Remove(device);
            }
            else
            {
                retryable++;
            }
        }

        if (accepted == 0 && retryable > 0)
        {
            db.ClearTracked();
            var reloaded = await db.OutboxMessages.SingleAsync(m => m.Id == message.Id, cancellationToken);
            return await RetryLaterAsync(reloaded, "the push service refused the message", now, cancellationToken);
        }

        message.MarkDispatched(now);
        await db.SaveChangesAsync(cancellationToken);
        return accepted > 0 ? Outcome.Sent : Outcome.Discarded;
    }

    private async Task<bool> IsStillEntitledAsync(NotificationPayload payload, Guid customerUserId, Guid businessId, CancellationToken cancellationToken)
    {
        if (payload.Audience == NotificationAudience.Customer)
        {
            return customerUserId == payload.RecipientUserId;
        }

        return await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId && b.OwnerUserId == payload.RecipientUserId, cancellationToken);
    }

    private async Task<Outcome> DiscardAsync(OutboxMessage message, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        message.Discard(reason, now);
        await db.SaveChangesAsync(cancellationToken);
        return Outcome.Discarded;
    }

    private async Task<Outcome> RetryLaterAsync(OutboxMessage message, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        message.MarkAttemptFailed(reason, now + OutboxMessage.BackoffFor(message.Attempts + 1));
        await db.SaveChangesAsync(cancellationToken);
        if (message.Attempts >= OutboxMessage.MaxAttempts)
        {
            logger.LogError("A {Type} notification failed {Attempts} times and was given up on: {Reason}", message.Type, message.Attempts, reason);
        }
        else
        {
            logger.LogWarning("A {Type} notification could not be sent ({Reason}); it will be tried again", message.Type, reason);
        }

        return Outcome.Retrying;
    }

    // --- Did it arrive? ---

    /// <summary>
    /// Expo accepting a message is not the phone receiving it. About 15 minutes later its receipt says whether it got through, or
    /// that the phone no longer has the app (then the token is deleted). Expo keeps receipts for 24 hours, so a message with none by
    /// then is recorded as failed. Returns how many receipts were applied.
    /// </summary>
    public async Task<int> CheckReceiptsAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !sender.IsAvailable)
        {
            return 0;
        }

        var now = time.GetUtcNow();
        var giveUpBefore = now - TimeSpan.FromHours(23);
        var waiting = await db.NotificationLog
            .Where(l => l.Status == NotificationStatus.Sent && l.ProviderTicketId != null && l.ReceiptCheckedAt == null && l.CreatedAt <= now - _options.ReceiptDelay)
            .OrderBy(l => l.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (waiting.Count == 0)
        {
            return 0;
        }

        var receipts = await sender.GetReceiptsAsync([.. waiting.Select(l => l.ProviderTicketId!)], cancellationToken);
        if (!receipts.IsSuccess)
        {
            logger.LogWarning("Could not fetch push receipts; they will be checked again");
            return 0;
        }

        var applied = 0;
        foreach (var entry in waiting)
        {
            if (!receipts.Value.TryGetValue(entry.ProviderTicketId!, out var receipt))
            {
                if (entry.CreatedAt < giveUpBefore)
                {
                    entry.MarkFailed("no receipt within 24 hours", now);
                    applied++;
                }

                continue;
            }

            applied++;
            if (receipt.Delivered)
            {
                entry.MarkDelivered(now);
                continue;
            }

            entry.MarkFailed(receipt.Error ?? "receipt reported an error", now);
            if (receipt.Error == DeviceNotRegistered && entry.DeviceTokenId is { } tokenId)
            {
                var device = await db.DeviceTokens.SingleOrDefaultAsync(d => d.Id == tokenId, cancellationToken);
                if (device is not null)
                {
                    db.DeviceTokens.Remove(device);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return applied;
    }
}

/// <summary>Queues a reminder the day before each confirmed booking starts (spec Phase 7: notifications, reminders).</summary>
public sealed class BookingReminders(IOnePondyDbContext db, NotificationOutbox outbox, TimeProvider time)
{
    /// <summary>A booking gets its reminder once it starts between 23 and 25 hours from now; a wider window than the job's hourly step, so none is missed.</summary>
    public static readonly TimeSpan WindowStart = TimeSpan.FromHours(23);

    public static readonly TimeSpan WindowEnd = TimeSpan.FromHours(25);

    public static string DedupeKey(Guid bookingId) => $"{NotificationTypes.BookingReminder}:{bookingId}";

    public async Task<int> EnqueueDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var from = now + WindowStart;
        var to = now + WindowEnd;
        var due = await db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed && b.StartAt >= from && b.StartAt < to)
            .Select(b => new { b.Id, b.CustomerUserId })
            .Take(500)
            .ToListAsync(cancellationToken);

        var queued = 0;
        foreach (var booking in due)
        {
            var key = DedupeKey(booking.Id);
            if (await db.OutboxMessages.AsNoTracking().AnyAsync(m => m.DedupeKey == key, cancellationToken))
            {
                continue;
            }

            outbox.Enqueue(NotificationTypes.BookingReminder, booking.CustomerUserId, NotificationAudience.Customer, booking.Id, key);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                queued++;
            }
            catch (Exception ex) when (WriteConflicts.Is(ex))
            {
                // Another run queued the same reminder at the same moment; the unique key kept it to one.
                db.ClearTracked();
            }
        }

        return queued;
    }
}

/// <summary>Deletes notification records once they have no further use (docs/data-retention.md).</summary>
public sealed class NotificationRetention(IOnePondyDbContext db, TimeProvider time)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public async Task<(int Logs, int Messages)> PurgeAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - Retention;
        var logs = await db.NotificationLog.Where(l => l.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        // Only a message no log row still points at: the log is removed first, but a message can be older than its log.
        var messages = await db.OutboxMessages
            .Where(m => m.CreatedAt < cutoff
                && (m.DispatchedAt != null || m.Attempts >= OutboxMessage.MaxAttempts)
                && !db.NotificationLog.Any(l => l.OutboxMessageId == m.Id))
            .ExecuteDeleteAsync(cancellationToken);
        return (logs, messages);
    }
}
