using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Bookings;
using OnePondy.Application.Notifications;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Notifications;
using OnePondy.Domain.Payments;

namespace OnePondy.UnitTests.Bookings;

/// <summary>Phase 7 step 7.7: what the outbox records, and who is sent what.</summary>
public class NotificationOutboxTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly PaymentHarness _h = new();

    private Guid Customer => _h.Bookings.Customer;

    private Guid Owner => _h.Bookings.Owner;

    private async Task<List<(string Type, NotificationPayload Payload)>> QueuedAsync() =>
        [.. (await _h.Bookings.Db.OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync())
            .Select(m => (m.Type, NotificationOutbox.Parse(m.Payload)!))];

    private async Task<BookingDetail> RequestAsync() =>
        (await _h.Bookings.Bookings.CreateAsync(Customer, _h.Bookings.Input(), None)).Value!;

    [Fact]
    public async Task A_new_request_tells_the_businesss_owner_and_only_them()
    {
        var booking = await RequestAsync();

        var queued = Assert.Single(await QueuedAsync());
        Assert.Equal(NotificationTypes.BookingRequested, queued.Type);
        Assert.Equal(Owner, queued.Payload.RecipientUserId);
        Assert.Equal(NotificationAudience.Partner, queued.Payload.Audience);
        Assert.Equal(booking.Id, queued.Payload.BookingId);
    }

    [Fact]
    public async Task Retrying_the_same_request_does_not_tell_the_owner_twice()
    {
        await RequestAsync();
        await RequestAsync();

        Assert.Single(await QueuedAsync());
    }

    [Fact]
    public async Task A_request_that_fails_for_lack_of_room_queues_nothing()
    {
        var result = await _h.Bookings.Bookings.CreateAsync(Customer, _h.Bookings.Input(guestCount: 5), None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await QueuedAsync());
    }

    [Fact]
    public async Task Accepting_a_free_or_unpaid_booking_confirms_it_and_tells_the_customer()
    {
        var booking = await RequestAsync();

        await _h.Bookings.Bookings.AcceptAsync(_h.Bookings.BusinessId, booking.Id, booking.Version, Owner, None);

        var told = (await QueuedAsync()).Single(q => q.Type != NotificationTypes.BookingRequested);
        Assert.Equal(NotificationTypes.BookingConfirmed, told.Type);
        Assert.Equal(Customer, told.Payload.RecipientUserId);
        Assert.Equal(NotificationAudience.Customer, told.Payload.Audience);
    }

    [Fact]
    public async Task Accepting_a_booking_that_needs_payment_tells_the_customer_to_pay()
    {
        await _h.AwaitingPaymentAsync();

        var told = (await QueuedAsync()).Single(q => q.Type != NotificationTypes.BookingRequested);
        Assert.Equal(NotificationTypes.BookingAccepted, told.Type);
        Assert.Equal(Customer, told.Payload.RecipientUserId);
    }

    [Fact]
    public async Task Rejecting_tells_the_customer()
    {
        var booking = await RequestAsync();

        await _h.Bookings.Bookings.RejectAsync(_h.Bookings.BusinessId, booking.Id, booking.Version, Owner, "Full", None);

        var told = (await QueuedAsync()).Single(q => q.Type == NotificationTypes.BookingRejected);
        Assert.Equal(Customer, told.Payload.RecipientUserId);
    }

    [Fact]
    public async Task A_customer_cancelling_tells_the_partner_not_the_customer()
    {
        var booking = await RequestAsync();

        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, Customer, null, booking.Version, None);

        var told = (await QueuedAsync()).Single(q => q.Type == NotificationTypes.BookingCancelled);
        Assert.Equal(Owner, told.Payload.RecipientUserId);
        Assert.Equal(NotificationAudience.Partner, told.Payload.Audience);
    }

    [Fact]
    public async Task A_partner_cancelling_a_paid_booking_tells_the_customer_once_not_again_for_the_refund_step()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();

        await _h.Bookings.Bookings.CancelForBusinessAsync(_h.Bookings.BusinessId, booking.Id, booking.Version, Owner, "Closed", None);

        var told = (await QueuedAsync()).Where(q => q.Type == NotificationTypes.BookingCancelled).ToList();
        var single = Assert.Single(told);
        Assert.Equal(Customer, single.Payload.RecipientUserId);
        Assert.Equal(NotificationAudience.Customer, single.Payload.Audience);
    }

    [Fact]
    public async Task An_expired_hold_tells_the_customer()
    {
        await RequestAsync();
        _h.Bookings.Time.Advance(TimeSpan.FromMinutes(31));

        await _h.Bookings.Expiry.ExpireDueHoldsAsync(None);

        var told = (await QueuedAsync()).Single(q => q.Type == NotificationTypes.BookingExpired);
        Assert.Equal(Customer, told.Payload.RecipientUserId);
    }

    [Fact]
    public async Task A_verified_payment_tells_the_customer_it_is_confirmed_and_the_partner_it_is_paid()
    {
        var (booking, _, _) = await _h.PaidAsync();

        var queued = await QueuedAsync();

        var confirmed = queued.Single(q => q.Type == NotificationTypes.BookingConfirmed);
        var paid = queued.Single(q => q.Type == NotificationTypes.BookingPaid);
        Assert.Equal(Customer, confirmed.Payload.RecipientUserId);
        Assert.Equal(Owner, paid.Payload.RecipientUserId);
        Assert.All(new[] { confirmed, paid }, q => Assert.Equal(booking.Id, q.Payload.BookingId));
    }

    [Fact]
    public async Task A_payment_reported_twice_tells_nobody_twice()
    {
        var (_, orderId, paymentId) = await _h.PaidAsync();
        var before = (await QueuedAsync()).Count;

        await _h.Payments.RecordCaptureAsync(orderId, paymentId, 1000m, None);

        Assert.Equal(before, (await QueuedAsync()).Count);
    }

    [Fact]
    public async Task A_refund_paid_out_tells_the_customer()
    {
        _h.Bookings.Payments.RefundsEnabled = true;
        var (booking, _, _) = await _h.PaidAsync();
        await _h.Bookings.Bookings.CancelMineAsync(booking.Id, Customer, null, booking.Version, None);
        var refund = await _h.Bookings.Db.Refunds.AsNoTracking().SingleAsync();

        await _h.Payments.RecordRefundProcessedAsync(refund.ProviderRefundId!, None);

        var told = (await QueuedAsync()).Single(q => q.Type == NotificationTypes.RefundProcessed);
        Assert.Equal(Customer, told.Payload.RecipientUserId);
    }

    [Fact]
    public async Task Completing_a_booking_sends_nothing()
    {
        var (booking, _, _) = await _h.PaidAsync();
        var before = (await QueuedAsync()).Count;

        await _h.Bookings.Bookings.CompleteAsync(_h.Bookings.BusinessId, booking.Id, booking.Version, Owner, None);

        Assert.Equal(before, (await QueuedAsync()).Count);
    }

    [Fact]
    public async Task A_change_that_is_refused_leaves_no_notification_behind()
    {
        var booking = await RequestAsync();
        var before = (await QueuedAsync()).Count;

        var stale = await _h.Bookings.Bookings.AcceptAsync(_h.Bookings.BusinessId, booking.Id, booking.Version + 5, Owner, None);

        Assert.False(stale.IsSuccess);
        Assert.Equal(before, (await QueuedAsync()).Count);
    }
}

public class NotificationTemplateTests
{
    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Every_kind_has_plain_generic_words(string type)
    {
        var (title, body) = NotificationTemplates.For(type, "en");

        Assert.Equal("OnePondy", title);
        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.DoesNotContain("{", body);
        // Nothing that could identify the booking on a locked screen: no digits (amounts, dates, times), no currency.
        Assert.DoesNotContain(body, ch => char.IsDigit(ch) || ch == '₹');
    }

    public static IEnumerable<object[]> AllTypes() => NotificationTypes.All.Select(t => new object[] { t });

    [Fact]
    public void A_language_with_no_translation_yet_falls_back_to_english()
    {
        Assert.Equal(NotificationTemplates.For(NotificationTypes.BookingConfirmed, "en"), NotificationTemplates.For(NotificationTypes.BookingConfirmed, "ta"));
        Assert.Equal(NotificationTemplates.For(NotificationTypes.BookingConfirmed, "en"), NotificationTemplates.For(NotificationTypes.BookingConfirmed, null));
    }

    [Fact]
    public void An_unknown_kind_still_says_something_harmless()
    {
        var (_, body) = NotificationTemplates.For("something.new", "en");

        Assert.Equal("You have an update on a booking.", body);
    }

    [Fact]
    public void Each_kind_has_its_own_words()
    {
        var bodies = NotificationTypes.All.Select(t => NotificationTemplates.For(t, "en").Body).ToList();

        Assert.Equal(bodies.Count, bodies.Distinct().Count());
    }
}

public class DeviceServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Phone = "ExponentPushToken[abcdefghijklmnopqrstuv]";

    private readonly BookingHarness _h = new();

    private DeviceService Devices => _h.Devices;

    [Fact]
    public async Task A_valid_token_is_registered_for_the_session()
    {
        var user = Guid.NewGuid();
        var session = Guid.NewGuid();

        var result = await Devices.RegisterAsync(user, session, Phone, "android", None);

        Assert.True(result.IsSuccess);
        var stored = await _h.Db.DeviceTokens.AsNoTracking().SingleAsync();
        Assert.Equal(result.Value!.Id, stored.Id);
        Assert.Equal((user, session, Phone, DevicePlatform.Android), (stored.UserId, stored.SessionId, stored.Token, stored.Platform));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("ExponentPushToken[]")]
    [InlineData("ExponentPushToken[short]")]
    [InlineData("FCMToken[abcdefghijklmnopqrstuv]")]
    public async Task A_token_that_is_not_an_expo_token_is_refused(string? token)
    {
        var result = await Devices.RegisterAsync(Guid.NewGuid(), Guid.NewGuid(), token, "android", None);

        Assert.Equal("validation_failed", result.Error!.Code);
        Assert.Empty(await _h.Db.DeviceTokens.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("windows")]
    [InlineData("")]
    public async Task An_unknown_platform_is_refused(string? platform)
    {
        var result = await Devices.RegisterAsync(Guid.NewGuid(), Guid.NewGuid(), Phone, platform, None);

        Assert.Equal("validation_failed", result.Error!.Code);
    }

    [Theory]
    [InlineData("android")]
    [InlineData("IOS")]
    [InlineData(" ios ")]
    public async Task Platforms_are_accepted_in_any_case(string platform)
    {
        Assert.True((await Devices.RegisterAsync(Guid.NewGuid(), Guid.NewGuid(), Phone, platform, None)).IsSuccess);
    }

    [Fact]
    public async Task Registering_the_same_phone_again_refreshes_it_instead_of_adding_another()
    {
        var user = Guid.NewGuid();
        var first = await Devices.RegisterAsync(user, Guid.NewGuid(), Phone, "android", None);
        _h.Time.Advance(TimeSpan.FromHours(1));
        var newSession = Guid.NewGuid();

        var second = await Devices.RegisterAsync(user, newSession, Phone, "android", None);

        Assert.Equal(first.Value!.Id, second.Value!.Id);
        var stored = await _h.Db.DeviceTokens.AsNoTracking().SingleAsync();
        Assert.Equal(newSession, stored.SessionId);
        Assert.True(stored.LastSeenAt > stored.CreatedAt);
    }

    [Fact]
    public async Task A_phone_that_changes_hands_moves_to_its_new_owner()
    {
        var previous = Guid.NewGuid();
        var next = Guid.NewGuid();
        await Devices.RegisterAsync(previous, Guid.NewGuid(), Phone, "android", None);

        await Devices.RegisterAsync(next, Guid.NewGuid(), Phone, "android", None);

        var stored = await _h.Db.DeviceTokens.AsNoTracking().SingleAsync();
        Assert.Equal(next, stored.UserId);
    }

    [Fact]
    public async Task An_account_keeps_at_most_ten_phones_dropping_the_least_recently_seen()
    {
        var user = Guid.NewGuid();
        var session = Guid.NewGuid();
        for (var i = 0; i < DeviceToken.MaxPerUser + 2; i++)
        {
            await Devices.RegisterAsync(user, session, $"ExponentPushToken[phone{i:D12}]", "android", None);
            _h.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var kept = await _h.Db.DeviceTokens.AsNoTracking().Where(d => d.UserId == user).Select(d => d.Token).ToListAsync();

        Assert.Equal(DeviceToken.MaxPerUser, kept.Count);
        Assert.DoesNotContain("ExponentPushToken[phone000000000000]", kept);
        Assert.DoesNotContain("ExponentPushToken[phone000000000001]", kept);
        Assert.Contains($"ExponentPushToken[phone{DeviceToken.MaxPerUser + 1:D12}]", kept);
    }

    [Fact]
    public async Task A_user_can_forget_their_own_phone_but_not_someone_elses()
    {
        var owner = Guid.NewGuid();
        var device = (await Devices.RegisterAsync(owner, Guid.NewGuid(), Phone, "android", None)).Value!;

        var byStranger = await Devices.UnregisterAsync(Guid.NewGuid(), device.Id, None);
        Assert.Equal("device_not_found", byStranger!.Code);
        Assert.Single(await _h.Db.DeviceTokens.ToListAsync());

        var byOwner = await Devices.UnregisterAsync(owner, device.Id, None);
        Assert.Null(byOwner);
        Assert.Empty(await _h.Db.DeviceTokens.ToListAsync());
        Assert.Equal("device_not_found", (await Devices.UnregisterAsync(owner, device.Id, None))!.Code);
    }
}
