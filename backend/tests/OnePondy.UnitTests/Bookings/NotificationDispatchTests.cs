using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Bookings;
using OnePondy.Application.Notifications;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Notifications;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>Phase 7 step 7.7: sending, retrying, dropping what must not be sent, and checking it arrived.</summary>
public class NotificationDispatchTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    private BookingHarness B => _h.Bookings;

    private Guid Customer => B.Customer;

    private Guid Owner => B.Owner;

    private FakePushSender Push => B.Push;

    private static string TokenOf(string name) => $"ExponentPushToken[{name.PadRight(22, 'x')}]";

    /// <summary>A phone signed in as this user: an active session and a registered token.</summary>
    private async Task<(UserSession Session, string Token)> PhoneAsync(Guid userId, string name)
    {
        var session = UserSession.Start(userId, "phone", B.Time.GetUtcNow(), TimeSpan.FromDays(30));
        B.Db.UserSessions.Add(session);
        await B.Db.SaveChangesAsync();
        var token = TokenOf(name);
        Assert.True((await B.Devices.RegisterAsync(userId, session.Id, token, "android", None)).IsSuccess);
        return (session, token);
    }

    private Task<DispatchResult> DispatchAsync(int batch = 50) => B.Dispatcher.DispatchAsync(batch, None);

    private async Task<List<OutboxMessage>> OutboxAsync() => await B.Db.OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync();

    private async Task<BookingDetail> RequestAsync(string key = "idem-1") =>
        (await B.Bookings.CreateAsync(Customer, B.Input(idempotencyKey: key), None)).Value!;

    // --- Who is sent what ---

    [Fact]
    public async Task Each_person_is_sent_generic_words_on_their_own_phone_and_nothing_about_the_booking_but_its_id()
    {
        var (_, customerPhone) = await PhoneAsync(Customer, "customer");
        var (_, partnerPhone) = await PhoneAsync(Owner, "partner");
        var booking = await _h.AwaitingPaymentAsync(); // the owner is told of the request, the customer that it was accepted

        var result = await DispatchAsync();

        Assert.Equal(new DispatchResult(2, 0, 0), result);
        var toPartner = Assert.Single(Push.Sent, m => m.To == partnerPhone);
        var toCustomer = Assert.Single(Push.Sent, m => m.To == customerPhone);
        Assert.Equal((NotificationTypes.BookingRequested, booking.Id), (toPartner.Type, toPartner.BookingId));
        Assert.Equal((NotificationTypes.BookingAccepted, booking.Id), (toCustomer.Type, toCustomer.BookingId));
        Assert.Equal(NotificationTemplates.For(NotificationTypes.BookingAccepted, "en").Body, toCustomer.Body);
        Assert.Equal("OnePondy", toCustomer.Title);
        Assert.DoesNotContain(booking.ListingTitle, toCustomer.Body);
        Assert.All(await OutboxAsync(), m => Assert.NotNull(m.DispatchedAt));
    }

    [Fact]
    public async Task What_was_sent_is_logged_without_any_message_text()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();

        await DispatchAsync();

        var log = Assert.Single(await B.Db.NotificationLog.AsNoTracking().ToListAsync());
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal(Customer, log.UserId);
        Assert.Equal(NotificationTypes.BookingAccepted, log.Type);
        Assert.StartsWith("ticket_", log.ProviderTicketId);
        Assert.Null(log.Error);
    }

    [Fact]
    public async Task Every_signed_in_phone_of_the_recipient_gets_it()
    {
        var (_, first) = await PhoneAsync(Customer, "first");
        var (_, second) = await PhoneAsync(Customer, "second");
        await _h.AwaitingPaymentAsync();

        await DispatchAsync();

        Assert.Equal(2, Push.Sent.Count(m => m.Type == NotificationTypes.BookingAccepted));
        Assert.Equal([first, second], Push.Sent.Where(m => m.Type == NotificationTypes.BookingAccepted).Select(m => m.To).Order());
    }

    [Fact]
    public async Task A_person_with_no_phone_registered_just_has_the_message_finished_without_sending()
    {
        await _h.AwaitingPaymentAsync();

        var result = await DispatchAsync();

        Assert.Equal(2, result.Discarded);
        Assert.Empty(Push.Sent);
        Assert.All(await OutboxAsync(), m =>
        {
            Assert.NotNull(m.DispatchedAt);
            Assert.Contains("no signed-in phone", m.LastError);
        });
    }

    [Fact]
    public async Task A_phone_whose_session_signed_out_gets_nothing()
    {
        var (session, _) = await PhoneAsync(Customer, "customer");
        session.Revoke(SessionRevocationReason.SignedOut, B.Time.GetUtcNow());
        await B.Db.SaveChangesAsync();
        await _h.AwaitingPaymentAsync();

        await DispatchAsync();

        Assert.Empty(Push.Sent);
    }

    [Fact]
    public async Task A_phone_whose_session_has_expired_gets_nothing()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();
        B.Time.Advance(TimeSpan.FromDays(31)); // past the session's life; the message is also too old, so use a fresh one

        await B.Bookings.CreateAsync(Customer, B.Input(idempotencyKey: "idem-late"), None);
        var result = await DispatchAsync();

        Assert.Empty(Push.Sent);
        Assert.True(result.Discarded >= 1);
    }

    [Fact]
    public async Task A_phone_that_changed_hands_never_receives_the_previous_owners_messages()
    {
        var stranger = Guid.NewGuid();
        var (_, token) = await PhoneAsync(Customer, "shared");
        var strangerSession = UserSession.Start(stranger, "phone", B.Time.GetUtcNow(), TimeSpan.FromDays(30));
        B.Db.UserSessions.Add(strangerSession);
        await B.Db.SaveChangesAsync();
        await B.Devices.RegisterAsync(stranger, strangerSession.Id, token, "android", None); // the same phone, signed in as someone else
        await _h.AwaitingPaymentAsync(); // the customer's "accepted" message

        await DispatchAsync();

        Assert.DoesNotContain(Push.Sent, m => m.Type == NotificationTypes.BookingAccepted);
    }

    // --- Entitlement is checked again when sending ---

    private async Task<Guid> QueueDirectlyAsync(string type, Guid recipient, NotificationAudience audience, Guid bookingId)
    {
        B.Notifications.Enqueue(type, recipient, audience, bookingId);
        await B.Db.SaveChangesAsync();
        return (await B.Db.OutboxMessages.AsNoTracking().OrderByDescending(m => m.CreatedAt).FirstAsync()).Id;
    }

    [Fact]
    public async Task A_customer_message_for_someone_who_is_not_the_bookings_customer_is_dropped_even_if_they_have_a_phone()
    {
        var stranger = Guid.NewGuid();
        await PhoneAsync(stranger, "stranger");
        var booking = await RequestAsync();
        await QueueDirectlyAsync(NotificationTypes.BookingConfirmed, stranger, NotificationAudience.Customer, booking.Id);

        await DispatchAsync();

        Assert.DoesNotContain(Push.Sent, m => m.To == TokenOf("stranger"));
        Assert.Contains("not entitled", (await OutboxAsync()).Last().LastError);
    }

    [Fact]
    public async Task A_partner_message_for_someone_who_does_not_own_the_business_is_dropped()
    {
        var stranger = Guid.NewGuid();
        await PhoneAsync(stranger, "stranger");
        var booking = await RequestAsync();
        await QueueDirectlyAsync(NotificationTypes.BookingRequested, stranger, NotificationAudience.Partner, booking.Id);

        await DispatchAsync();

        Assert.DoesNotContain(Push.Sent, m => m.To == TokenOf("stranger"));
    }

    [Fact]
    public async Task A_customer_message_sent_to_the_partner_audience_is_dropped_because_the_customer_does_not_own_the_business()
    {
        await PhoneAsync(Customer, "customer");
        var booking = await RequestAsync();
        await QueueDirectlyAsync(NotificationTypes.BookingRequested, Customer, NotificationAudience.Partner, booking.Id);

        await DispatchAsync();

        Assert.DoesNotContain(Push.Sent, m => m.Type == NotificationTypes.BookingRequested && m.To == TokenOf("customer"));
    }

    [Fact]
    public async Task A_closed_account_is_sent_nothing()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();
        await B.Accounts.AnonymizeAsync(Customer, B.Time.GetUtcNow(), None);

        await DispatchAsync();

        Assert.DoesNotContain(Push.Sent, m => m.To == TokenOf("customer"));
    }

    [Fact]
    public async Task A_message_about_a_booking_that_does_not_exist_is_dropped()
    {
        await PhoneAsync(Customer, "customer");
        await QueueDirectlyAsync(NotificationTypes.BookingConfirmed, Customer, NotificationAudience.Customer, Guid.NewGuid());

        var result = await DispatchAsync();

        Assert.Equal(1, result.Discarded);
        Assert.Empty(Push.Sent);
    }

    [Fact]
    public async Task A_pay_now_message_is_dropped_once_the_booking_is_no_longer_waiting_for_payment()
    {
        await PhoneAsync(Customer, "customer");
        var (booking, _, _) = await _h.PaidAsync(); // accepted, then paid before the dispatcher ran

        await DispatchAsync();

        Assert.DoesNotContain(Push.Sent, m => m.Type == NotificationTypes.BookingAccepted);
        Assert.Contains(Push.Sent, m => m.Type == NotificationTypes.BookingConfirmed && m.BookingId == booking.Id);
    }

    // --- Switched off ---

    [Fact]
    public async Task While_notifications_are_off_nothing_is_sent_and_the_messages_stay_queued()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();
        B.NotificationSettings.Enabled = false;

        var result = await DispatchAsync();

        Assert.Equal(new DispatchResult(0, 0, 0), result);
        Assert.Empty(Push.Sent);
        Assert.All(await OutboxAsync(), m => Assert.Null(m.DispatchedAt));
    }

    [Fact]
    public async Task With_no_push_service_set_up_nothing_is_touched()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();
        Push.IsAvailable = false;

        await DispatchAsync();

        Assert.All(await OutboxAsync(), m =>
        {
            Assert.Null(m.DispatchedAt);
            Assert.Equal(0, m.Attempts);
        });
    }

    [Fact]
    public async Task Messages_that_waited_longer_than_they_are_useful_are_dropped_not_delivered_late()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();
        B.Time.Advance(TimeSpan.FromHours(25));

        var result = await DispatchAsync();

        Assert.Equal(2, result.Discarded);
        Assert.Empty(Push.Sent);
    }

    // --- Failures and retries ---

    [Fact]
    public async Task A_failed_send_is_retried_after_a_back_off_and_succeeds_when_the_service_is_back()
    {
        await PhoneAsync(Customer, "customer");
        await RequestAsync();
        await _h.Bookings.Bookings.RejectAsync(B.BusinessId, (await B.Db.Bookings.AsNoTracking().SingleAsync()).Id, (await B.Db.Bookings.AsNoTracking().SingleAsync()).Version, Owner, "Full", None);
        Push.SendFails = true;

        var first = await DispatchAsync();
        Assert.True(first.Retrying >= 1);
        var queued = (await OutboxAsync()).Single(m => m.Type == NotificationTypes.BookingRejected);
        Assert.Equal(1, queued.Attempts);
        Assert.Equal(B.Time.GetUtcNow() + TimeSpan.FromMinutes(1), queued.NextAttemptAt);

        // Not due yet: nothing is tried.
        Push.SendFails = false;
        Assert.Equal(0, (await DispatchAsync()).Sent);

        B.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, (await DispatchAsync()).Sent);
        Assert.Single(Push.Sent, m => m.Type == NotificationTypes.BookingRejected);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_given_up_on_after_five_attempts()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();
        Push.SendFails = true;

        for (var attempt = 1; attempt <= OutboxMessage.MaxAttempts; attempt++)
        {
            await DispatchAsync();
            B.Time.Advance(TimeSpan.FromHours(2)); // longer than any back-off, still inside the message's useful life
        }

        var accepted = (await OutboxAsync()).Single(m => m.Type == NotificationTypes.BookingAccepted);
        Assert.Equal(OutboxMessage.MaxAttempts, accepted.Attempts);
        Assert.False(accepted.IsPending);
        Assert.Null(accepted.DispatchedAt);

        Push.SendFails = false;
        var after = await DispatchAsync();
        Assert.DoesNotContain(Push.Sent, m => m.Type == NotificationTypes.BookingAccepted);
        Assert.Equal(0, after.Sent);
    }

    [Fact]
    public async Task The_back_off_grows_one_five_thirty_minutes_then_two_hours()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), OutboxMessage.BackoffFor(1));
        Assert.Equal(TimeSpan.FromMinutes(5), OutboxMessage.BackoffFor(2));
        Assert.Equal(TimeSpan.FromMinutes(30), OutboxMessage.BackoffFor(3));
        Assert.Equal(TimeSpan.FromHours(2), OutboxMessage.BackoffFor(4));
        Assert.Equal(TimeSpan.FromHours(2), OutboxMessage.BackoffFor(5));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_phone_expo_says_no_longer_has_the_app_is_forgotten()
    {
        var (_, dead) = await PhoneAsync(Customer, "dead");
        Push.TokenErrors[dead] = "DeviceNotRegistered";
        await _h.AwaitingPaymentAsync();

        var result = await DispatchAsync();

        Assert.Equal(0, result.Sent);
        Assert.Empty(await B.Db.DeviceTokens.ToListAsync());
        var log = Assert.Single(await B.Db.NotificationLog.AsNoTracking().ToListAsync());
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal("DeviceNotRegistered", log.Error);
        Assert.All(await OutboxAsync(), m => Assert.NotNull(m.DispatchedAt));
    }

    [Fact]
    public async Task When_one_phone_is_gone_and_another_works_the_message_counts_as_sent_and_the_dead_phone_is_forgotten()
    {
        var (_, dead) = await PhoneAsync(Customer, "dead");
        var (_, alive) = await PhoneAsync(Customer, "alive");
        Push.TokenErrors[dead] = "DeviceNotRegistered";
        await _h.AwaitingPaymentAsync();

        var result = await DispatchAsync();

        Assert.True(result.Sent >= 1);
        Assert.Equal([alive], (await B.Db.DeviceTokens.AsNoTracking().Select(d => d.Token).ToListAsync()));
    }

    [Fact]
    public async Task A_refusal_that_is_not_about_the_phone_is_retried_and_the_phone_kept()
    {
        var (_, token) = await PhoneAsync(Customer, "customer");
        Push.TokenErrors[token] = "MessageRateExceeded";
        await _h.AwaitingPaymentAsync();

        var result = await DispatchAsync();

        Assert.True(result.Retrying >= 1);
        Assert.Single(await B.Db.DeviceTokens.ToListAsync());
        var accepted = (await OutboxAsync()).Single(m => m.Type == NotificationTypes.BookingAccepted);
        Assert.Equal(1, accepted.Attempts);
        Assert.Null(accepted.DispatchedAt);
    }

    // --- Sent once ---

    [Fact]
    public async Task A_message_already_sent_is_not_sent_again()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();
        await DispatchAsync();
        var sent = Push.Sent.Count;

        await DispatchAsync();
        await DispatchAsync();

        Assert.Equal(sent, Push.Sent.Count);
    }

    [Fact]
    public async Task A_message_another_dispatcher_is_holding_is_left_alone_until_its_hold_runs_out()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();
        foreach (var message in await B.Db.OutboxMessages.ToListAsync())
        {
            message.Lease(B.Time.GetUtcNow() + OutboxDispatcher.LeaseDuration);
        }

        await B.Db.SaveChangesAsync();

        Assert.Equal(0, (await DispatchAsync()).Sent);

        B.Time.Advance(OutboxDispatcher.LeaseDuration + TimeSpan.FromSeconds(1)); // the other dispatcher died
        Assert.True((await DispatchAsync()).Sent >= 1);
    }

    [Fact]
    public async Task The_oldest_messages_go_first_and_a_batch_stops_at_its_size()
    {
        await PhoneAsync(Customer, "customer");
        var first = await RequestAsync("idem-1");
        B.Time.Advance(TimeSpan.FromSeconds(1));
        await B.Bookings.RejectAsync(B.BusinessId, first.Id, first.Version, Owner, "Full", None);
        B.Time.Advance(TimeSpan.FromSeconds(1));
        var second = await RequestAsync("idem-2");
        await B.Bookings.RejectAsync(B.BusinessId, second.Id, second.Version, Owner, "Full", None);

        var done = await DispatchAsync(batch: 2);

        Assert.Equal(2, done.Sent + done.Discarded);
        Assert.Equal(2, (await OutboxAsync()).Count(m => m.DispatchedAt is not null));
    }

    // --- Did it arrive? ---

    private async Task<NotificationLogEntry> SentAndReadyForReceiptAsync()
    {
        await PhoneAsync(Customer, "customer");
        await _h.AwaitingPaymentAsync();
        await DispatchAsync();
        return await B.Db.NotificationLog.SingleAsync();
    }

    [Fact]
    public async Task Receipts_are_not_asked_for_until_fifteen_minutes_have_passed()
    {
        await SentAndReadyForReceiptAsync();
        B.Time.Advance(TimeSpan.FromMinutes(14));

        Assert.Equal(0, await B.Dispatcher.CheckReceiptsAsync(50, None));

        Assert.Equal(0, Push.ReceiptRequests);
    }

    [Fact]
    public async Task A_receipt_that_says_ok_marks_the_notification_delivered_and_is_not_asked_for_again()
    {
        var log = await SentAndReadyForReceiptAsync();
        Push.Receipts[log.ProviderTicketId!] = new PushReceipt(true, null);
        B.Time.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(1, await B.Dispatcher.CheckReceiptsAsync(50, None));

        var after = await B.Db.NotificationLog.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationStatus.Delivered, after.Status);
        Assert.NotNull(after.ReceiptCheckedAt);
        Assert.Equal(0, await B.Dispatcher.CheckReceiptsAsync(50, None));
        Assert.Equal(1, Push.ReceiptRequests);
    }

    [Fact]
    public async Task A_receipt_saying_the_phone_no_longer_has_the_app_fails_it_and_forgets_the_phone()
    {
        var log = await SentAndReadyForReceiptAsync();
        Push.Receipts[log.ProviderTicketId!] = new PushReceipt(false, "DeviceNotRegistered");
        B.Time.Advance(TimeSpan.FromMinutes(16));

        await B.Dispatcher.CheckReceiptsAsync(50, None);

        var after = await B.Db.NotificationLog.AsNoTracking().SingleAsync();
        Assert.Equal((NotificationStatus.Failed, "DeviceNotRegistered"), (after.Status, after.Error));
        Assert.Empty(await B.Db.DeviceTokens.ToListAsync());
    }

    [Fact]
    public async Task A_notification_with_no_receipt_yet_is_left_waiting_and_failed_only_after_a_day()
    {
        await SentAndReadyForReceiptAsync();
        B.Time.Advance(TimeSpan.FromMinutes(20));

        await B.Dispatcher.CheckReceiptsAsync(50, None);
        Assert.Equal(NotificationStatus.Sent, (await B.Db.NotificationLog.AsNoTracking().SingleAsync()).Status);

        B.Time.Advance(TimeSpan.FromHours(24));
        await B.Dispatcher.CheckReceiptsAsync(50, None);

        var after = await B.Db.NotificationLog.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationStatus.Failed, after.Status);
        Assert.Equal("no receipt within 24 hours", after.Error);
    }

    [Fact]
    public async Task When_receipts_cannot_be_fetched_nothing_changes_and_it_is_tried_again_later()
    {
        var log = await SentAndReadyForReceiptAsync();
        Push.Receipts[log.ProviderTicketId!] = new PushReceipt(true, null);
        Push.ReceiptsFail = true;
        B.Time.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(0, await B.Dispatcher.CheckReceiptsAsync(50, None));
        Assert.Equal(NotificationStatus.Sent, (await B.Db.NotificationLog.AsNoTracking().SingleAsync()).Status);

        Push.ReceiptsFail = false;
        Assert.Equal(1, await B.Dispatcher.CheckReceiptsAsync(50, None));
    }

    // --- Reminders ---

    [Fact]
    public async Task A_confirmed_booking_starting_tomorrow_gets_one_reminder_however_often_the_job_runs()
    {
        await _h.PaidAsync(); // starts in 24 hours

        var first = await B.Reminders.EnqueueDueAsync(None);
        var second = await B.Reminders.EnqueueDueAsync(None);
        B.Time.Advance(TimeSpan.FromMinutes(30));
        var third = await B.Reminders.EnqueueDueAsync(None);

        Assert.Equal((1, 0, 0), (first, second, third));
        var reminder = Assert.Single(await OutboxAsync(), m => m.Type == NotificationTypes.BookingReminder);
        Assert.Equal(Customer, NotificationOutbox.Parse(reminder.Payload)!.RecipientUserId);
    }

    [Fact]
    public async Task Bookings_not_due_or_not_confirmed_get_no_reminder()
    {
        await _h.AwaitingPaymentAsync(); // accepted but not paid: not confirmed
        Assert.Equal(0, await B.Reminders.EnqueueDueAsync(None));

        var (paid, _, _) = await _h.PaidAsync("idem-paid");
        B.Time.Advance(TimeSpan.FromHours(14)); // now it starts in 10 hours
        Assert.Equal(0, await B.Reminders.EnqueueDueAsync(None));
        Assert.Equal(BookingStatus.Confirmed, (await _h.BookingAsync(paid.Id)).Status);
    }

    [Fact]
    public async Task The_reminder_reaches_the_customers_phone_with_generic_words()
    {
        var (_, token) = await PhoneAsync(Customer, "customer");
        var (booking, _, _) = await _h.PaidAsync();
        await DispatchAsync(); // the confirmation
        await B.Reminders.EnqueueDueAsync(None);

        await DispatchAsync();

        var reminder = Assert.Single(Push.Sent, m => m.Type == NotificationTypes.BookingReminder);
        Assert.Equal((token, booking.Id, "You have a booking tomorrow."), (reminder.To, reminder.BookingId, reminder.Body));
    }

    [Fact]
    public async Task A_reminder_is_not_sent_if_the_booking_was_cancelled_in_the_meantime()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        await PhoneAsync(Customer, "customer");
        var (booking, _, _) = await _h.PaidAsync();
        await B.Reminders.EnqueueDueAsync(None);
        await B.Bookings.CancelMineAsync(booking.Id, Customer, null, booking.Version, None);

        await DispatchAsync();

        Assert.DoesNotContain(Push.Sent, m => m.Type == NotificationTypes.BookingReminder);
    }
}
