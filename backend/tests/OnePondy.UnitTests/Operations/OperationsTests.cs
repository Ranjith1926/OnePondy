using Microsoft.Extensions.Options;
using OnePondy.Application.Food;
using OnePondy.Application.Notifications;
using OnePondy.Application.Operations;
using OnePondy.Application.Payments;
using OnePondy.Application.Rides;
using OnePondy.Application.Support;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Payments;
using OnePondy.Domain.Reviews;
using OnePondy.Domain.Support;
using OnePondy.UnitTests.Food;

namespace OnePondy.UnitTests.Operations;

/// <summary>The alert rules: pure, so the same numbers always give the same alerts.</summary>
public class OperationsAlertTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly OperationsOptions Settings = new();

    private static readonly SupportCounts NoSupport = new(0, 0, 0, 0, 0);

    private static IReadOnlyList<OperationsAlert> Alerts(
        ReviewBacklog? reviews = null,
        SupportCounts? support = null,
        int orders = 0,
        int exceptions = 0,
        NotificationBacklog? notifications = null,
        SignInActivity? signIn = null,
        params RateLimitClassStatistics[] limits) =>
        OperationsService.Alerts(
            Now,
            Settings,
            reviews ?? new ReviewBacklog(0, null),
            new SupportBacklog(support ?? NoSupport, null),
            orders,
            exceptions,
            notifications ?? new NotificationBacklog(true, 0, null),
            signIn ?? new SignInActivity(0, 0),
            new RateLimitSnapshot(Now.AddHours(-3), limits));

    [Fact]
    public void A_quiet_platform_has_no_alerts() => Assert.Empty(Alerts());

    [Fact]
    public void An_open_ride_emergency_is_critical_and_comes_first_and_incidents_are_a_warning()
    {
        var alerts = OperationsService.Alerts(
            Now,
            Settings,
            new ReviewBacklog(0, null),
            new SupportBacklog(NoSupport, null),
            2,
            0,
            new NotificationBacklog(true, 0, null),
            new SignInActivity(0, 0),
            new RateLimitSnapshot(Now.AddHours(-3), []),
            new OnePondy.Application.Rides.SafetyCounts(OpenSos: 1, OpenIncidents: 3, OldestOpenAt: Now.AddMinutes(-4)));

        Assert.Equal(["ride_sos", "orders_attention", "ride_incidents"], alerts.Select(a => a.Code));
        Assert.Equal(AlertSeverity.Critical, alerts[0].Severity);
        Assert.Contains("1 emergency alert from a ride is waiting for someone to deal with it", alerts[0].Message);
        Assert.Contains("3 ride incident reports are waiting", alerts[2].Message);
        Assert.All(new[] { alerts[0], alerts[2] }, a => Assert.Equal("safety", a.Area));
    }

    [Fact]
    public void Open_payment_differences_are_critical_and_point_at_payments()
    {
        var alert = Assert.Single(Alerts(exceptions: 2));

        Assert.Equal(("payment_exceptions", AlertSeverity.Critical, "payments"), (alert.Code, alert.Severity, alert.Area));
        Assert.Contains("2 payment differences need resolving", alert.Message);
    }

    [Fact]
    public void A_single_item_is_worded_in_the_singular()
    {
        Assert.Contains("1 payment difference needs resolving", Assert.Single(Alerts(exceptions: 1)).Message);
        Assert.Contains("1 food order looks stuck", Assert.Single(Alerts(orders: 1)).Message);
        Assert.Contains("1 support request has been escalated", Assert.Single(Alerts(support: new SupportCounts(0, 0, 0, 0, 1))).Message);
    }

    [Fact]
    public void Escalated_support_requests_are_critical_and_overdue_ones_a_warning()
    {
        var alerts = Alerts(support: new SupportCounts(NeedsReply: 4, Waiting: 1, Unassigned: 3, Overdue: 2, Escalated: 1));

        Assert.Equal(["support_escalated", "support_overdue"], alerts.Select(a => a.Code));
        Assert.Equal([AlertSeverity.Critical, AlertSeverity.Warning], alerts.Select(a => a.Severity));
        Assert.All(alerts, a => Assert.Equal("support", a.Area));
    }

    [Fact]
    public void Stuck_food_orders_are_a_warning_that_points_at_orders()
    {
        var alert = Assert.Single(Alerts(orders: 3));

        Assert.Equal(("orders_attention", AlertSeverity.Warning, "orders"), (alert.Code, alert.Severity, alert.Area));
    }

    [Fact]
    public void A_review_is_flagged_only_once_the_oldest_has_waited_past_the_limit_and_says_for_how_long()
    {
        Assert.Empty(Alerts(reviews: new ReviewBacklog(3, Now.AddDays(-1))));
        Assert.Empty(Alerts(reviews: new ReviewBacklog(0, null)));

        var alert = Assert.Single(Alerts(reviews: new ReviewBacklog(3, Now.AddDays(-3))));

        Assert.Equal("reviews_waiting", alert.Code);
        Assert.Contains("3 reviews have been waiting", alert.Message);
        Assert.Contains("3 days", alert.Message);
    }

    [Fact]
    public void Unsent_notifications_are_flagged_only_while_sending_is_switched_on()
    {
        var stale = Now.AddMinutes(-40);

        Assert.Empty(Alerts(notifications: new NotificationBacklog(Sending: false, Unsent: 9, OldestUnsentAt: stale)));
        Assert.Empty(Alerts(notifications: new NotificationBacklog(Sending: true, Unsent: 2, OldestUnsentAt: Now.AddMinutes(-5))));

        var alert = Assert.Single(Alerts(notifications: new NotificationBacklog(Sending: true, Unsent: 9, OldestUnsentAt: stale)));

        Assert.Equal("notifications_backlog", alert.Code);
        Assert.Contains("40 minutes", alert.Message);
    }

    [Fact]
    public void A_burst_of_sign_in_codes_is_flagged_above_the_limit_and_not_at_it()
    {
        Assert.Empty(Alerts(signIn: new SignInActivity(Settings.SignInCodesPerHourWarn, 500)));

        var alert = Assert.Single(Alerts(signIn: new SignInActivity(Settings.SignInCodesPerHourWarn + 1, 500)));

        Assert.Equal("sign_in_abuse", alert.Code);
    }

    [Fact]
    public void A_class_of_request_that_has_been_refused_often_is_flagged_by_name_busiest_first()
    {
        var alerts = Alerts(limits:
        [
            new RateLimitClassStatistics("browse", 900, 60, Now),
            new RateLimitClassStatistics("create", 40, Settings.RateLimitRefusalsWarn - 1, Now),
            new RateLimitClassStatistics("auth", 300, 200, Now),
        ]);

        Assert.Equal(["rate_limit_auth", "rate_limit_browse"], alerts.Select(a => a.Code));
        Assert.Contains("200 auth requests", alerts[0].Message);
    }

    [Fact]
    public void The_most_serious_alerts_come_first()
    {
        var alerts = Alerts(
            orders: 2,
            exceptions: 1,
            support: new SupportCounts(1, 0, 0, 1, 1),
            signIn: new SignInActivity(Settings.SignInCodesPerHourWarn + 5, 0));

        Assert.Equal(
            [AlertSeverity.Critical, AlertSeverity.Critical, AlertSeverity.Warning, AlertSeverity.Warning, AlertSeverity.Warning],
            alerts.Select(a => a.Severity));
    }
}

/// <summary>The snapshot against an in-memory platform: what it reads, and that it reads the right thing.</summary>
public class OperationsSnapshotTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class FixedStatistics(RateLimitSnapshot snapshot) : IRateLimitStatistics
    {
        public RateLimitSnapshot Snapshot() => snapshot;
    }

    private readonly FoodOrderHarness _h = new();
    private readonly SupportOptions _support = new();
    private readonly NotificationOptions _notifications = new();
    private readonly OperationsOptions _settings = new();
    private RateLimitSnapshot _limits;
    private readonly OperationsService _operations;
    private readonly SupportService _supportService;

    public OperationsSnapshotTests()
    {
        _limits = new RateLimitSnapshot(_h.B.Time.GetUtcNow(), []);
        _supportService = new SupportService(_h.Db, _h.B.Audit, _h.B.Accounts, Options.Create(_support), _h.B.Time);
        var orders = new FoodOrderAdminService(_h.Db, _h.Orders, _h.B.Accounts, _h.B.Audit, Options.Create(_h.Food), _h.B.Time);
        var reconciliation = new ReconciliationExceptionService(_h.Db, _h.B.Audit, _h.B.Time);
        _operations = new OperationsService(
            _h.Db,
            _supportService,
            orders,
            reconciliation,
            RideSafety(),
            new FixedStatistics(_limits),
            Options.Create(_settings),
            Options.Create(_notifications),
            _h.B.Time);
    }

    private DateTimeOffset Now => _h.B.Time.GetUtcNow();

    private RideSafetyService RideSafety() => new(
        _h.Db,
        _h.B.Accounts,
        new OnePondy.Infrastructure.Security.HmacSecretHasher(Options.Create(new OnePondy.Infrastructure.Security.SecretHashingOptions { HashingKey = OnePondy.UnitTests.Identity.TestKeys.Hashing })),
        _h.B.Audit,
        Options.Create(new RideOptions()),
        _h.B.Time);

    [Fact]
    public async Task A_new_platform_has_nothing_to_report()
    {
        var snapshot = await _operations.SnapshotAsync(None);

        Assert.Empty(snapshot.Alerts);
        Assert.Equal(new ReviewBacklog(0, null), snapshot.Reviews);
        Assert.Equal(new SupportCounts(0, 0, 0, 0, 0), snapshot.Support.Counts);
        Assert.Equal(0, snapshot.FoodOrdersNeedingAttention);
        Assert.Equal(0, snapshot.OpenReconciliationExceptions);
        Assert.Equal(0, snapshot.Notifications.Unsent);
        Assert.Equal(new SignInActivity(0, 0), snapshot.SignIn);
        Assert.Equal(Now, snapshot.GeneratedAt);
    }

    [Fact]
    public async Task Waiting_reviews_are_counted_with_the_oldest_and_published_ones_are_not()
    {
        var customer = Guid.NewGuid();
        _h.Db.Reviews.Add(Review.ForFoodOrder(customer, Guid.NewGuid(), Guid.NewGuid(), 5, null, "Anita", null, Now, Now.AddDays(-3)));
        _h.Db.Reviews.Add(Review.ForFoodOrder(customer, Guid.NewGuid(), Guid.NewGuid(), 4, null, "Anita", null, Now, Now.AddDays(-1)));
        var published = Review.ForFoodOrder(customer, Guid.NewGuid(), Guid.NewGuid(), 3, null, "Anita", null, Now, Now.AddDays(-9));
        published.Publish(Guid.NewGuid(), Now.AddDays(-8));
        _h.Db.Reviews.Add(published);
        await _h.Db.SaveChangesAsync(None);

        var snapshot = await _operations.SnapshotAsync(None);

        Assert.Equal(2, snapshot.Reviews.Pending);
        Assert.Equal(Now.AddDays(-3), snapshot.Reviews.OldestPendingAt);
        Assert.Contains(snapshot.Alerts, a => a.Code == "reviews_waiting");
    }

    [Fact]
    public async Task Support_requests_are_counted_with_the_oldest_waiting_for_a_reply_and_flagged_when_escalated()
    {
        var person = Guid.NewGuid();
        var old = SupportTicket.Open(person, null, SupportTopic.Other, "Old", "Help", null, null, Now.AddHours(-30));
        var recent = SupportTicket.Open(person, null, SupportTopic.Other, "Recent", "Help", null, null, Now.AddHours(-1));
        old.Escalate(SupportTicket.Escalations.FirstReplyOverdue, Now);
        _h.Db.SupportTickets.Add(old);
        _h.Db.SupportTickets.Add(recent);
        await _h.Db.SaveChangesAsync(None);

        var snapshot = await _operations.SnapshotAsync(None);

        Assert.Equal(2, snapshot.Support.Counts.NeedsReply);
        Assert.Equal(1, snapshot.Support.Counts.Escalated);
        Assert.Equal(1, snapshot.Support.Counts.Overdue);
        Assert.Equal(Now.AddHours(-30), snapshot.Support.OldestNeedingReplyAt);
        Assert.Contains(snapshot.Alerts, a => a.Code == "support_escalated" && a.Severity == AlertSeverity.Critical);
    }

    [Fact]
    public async Task Open_payment_differences_are_counted_and_critical()
    {
        _h.Db.ReconciliationExceptions.Add(ReconciliationException.Detect(
            ReconciliationKind.PaymentMismatch, "fp-1", "Amounts differ.", Now, expectedAmount: 100m, actualAmount: 90m));
        await _h.Db.SaveChangesAsync(None);

        var snapshot = await _operations.SnapshotAsync(None);

        Assert.Equal(1, snapshot.OpenReconciliationExceptions);
        Assert.Contains(snapshot.Alerts, a => a.Code == "payment_exceptions" && a.Severity == AlertSeverity.Critical);
    }

    [Fact]
    public async Task The_notification_backlog_counts_unsent_messages_and_alerts_only_when_sending_is_on()
    {
        _h.Db.OutboxMessages.Add(OutboxMessage.Create("booking_confirmed", "{}", Now.AddHours(-2)));
        var sent = OutboxMessage.Create("booking_confirmed", "{}", Now.AddHours(-3));
        sent.MarkDispatched(Now.AddHours(-3));
        _h.Db.OutboxMessages.Add(sent);
        await _h.Db.SaveChangesAsync(None);

        var off = await _operations.SnapshotAsync(None);
        Assert.Equal(1, off.Notifications.Unsent);
        Assert.Equal(Now.AddHours(-2), off.Notifications.OldestUnsentAt);
        Assert.False(off.Notifications.Sending);
        Assert.DoesNotContain(off.Alerts, a => a.Code == "notifications_backlog");

        _notifications.Enabled = true;
        var on = await _operations.SnapshotAsync(None);
        Assert.Contains(on.Alerts, a => a.Code == "notifications_backlog");
    }

    [Fact]
    public async Task Sign_in_codes_are_counted_for_the_last_hour_and_the_last_day()
    {
        void Code(TimeSpan ago) => _h.Db.OtpChallenges.Add(OtpChallenge.Create(
            OtpChannel.Sms, OtpPurpose.SignIn, "+919800000000", [1, 2, 3], Now - ago, TimeSpan.FromMinutes(10)));
        Code(TimeSpan.FromMinutes(5));
        Code(TimeSpan.FromMinutes(50));
        Code(TimeSpan.FromHours(5));
        Code(TimeSpan.FromHours(30));
        await _h.Db.SaveChangesAsync(None);

        var snapshot = await _operations.SnapshotAsync(None);

        Assert.Equal(new SignInActivity(CodesLastHour: 2, CodesLast24Hours: 3), snapshot.SignIn);
    }

    [Fact]
    public async Task What_the_rate_limiter_reports_is_passed_through()
    {
        var limits = new RateLimitSnapshot(Now.AddHours(-2), [new RateLimitClassStatistics("create", 30, 4, Now)]);
        var service = new OperationsService(
            _h.Db,
            _supportService,
            new FoodOrderAdminService(_h.Db, _h.Orders, _h.B.Accounts, _h.B.Audit, Options.Create(_h.Food), _h.B.Time),
            new ReconciliationExceptionService(_h.Db, _h.B.Audit, _h.B.Time),
            RideSafety(),
            new FixedStatistics(limits),
            Options.Create(_settings),
            Options.Create(_notifications),
            _h.B.Time);

        var snapshot = await service.SnapshotAsync(None);

        Assert.Equal(limits, snapshot.RateLimits);
    }
}
