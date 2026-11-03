using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Food;
using OnePondy.Application.Notifications;
using OnePondy.Application.Payments;
using OnePondy.Application.Support;
using OnePondy.Domain.Reviews;
using OnePondy.Domain.Support;

namespace OnePondy.Application.Operations;

/// <summary>When the operations page should start pointing at something. Every value is a setting (<c>Operations</c>).</summary>
public sealed class OperationsOptions
{
    public const string SectionName = "Operations";

    /// <summary>A review waiting longer than this for a moderator is flagged.</summary>
    public TimeSpan ReviewBacklogWarnAfter { get; set; } = TimeSpan.FromDays(2);

    /// <summary>A notification waiting longer than this to be sent is flagged (only while sending is switched on).</summary>
    public TimeSpan NotificationBacklogWarnAfter { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>More sign-in codes than this requested in an hour looks like someone abusing the sign-in, and is flagged.</summary>
    public int SignInCodesPerHourWarn { get; set; } = 200;

    /// <summary>A class of request refused this many times since the API started is flagged.</summary>
    public int RateLimitRefusalsWarn { get; set; } = 50;
}

public enum AlertSeverity
{
    /// <summary>Worth a look today.</summary>
    Warning = 1,

    /// <summary>Money, safety or a promise to someone is at stake: look now.</summary>
    Critical = 2,
}

/// <summary>Something that needs attention, in words, with where to go for it. <c>Area</c> names a page of the console.</summary>
public sealed record OperationsAlert(string Code, AlertSeverity Severity, string Message, string Area);

public sealed record ReviewBacklog(int Pending, DateTimeOffset? OldestPendingAt);

public sealed record SupportBacklog(SupportCounts Counts, DateTimeOffset? OldestNeedingReplyAt);

public sealed record NotificationBacklog(bool Sending, int Unsent, DateTimeOffset? OldestUnsentAt);

public sealed record SignInActivity(int CodesLastHour, int CodesLast24Hours);

public sealed record OperationsSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<OperationsAlert> Alerts,
    ReviewBacklog Reviews,
    SupportBacklog Support,
    int FoodOrdersNeedingAttention,
    int OpenReconciliationExceptions,
    NotificationBacklog Notifications,
    SignInActivity SignIn,
    RateLimitSnapshot RateLimits);

/// <summary>
/// What an administrator should look at, in one place (spec Phase 10, step 10.5). It adds nothing new to the data: it counts what other parts of the
/// platform already keep (waiting reviews, support requests, orders and payments needing attention, the notification backlog, sign-in codes, refused
/// requests) and turns thresholds from settings into a short list of alerts. Delivering an alert by e-mail or chat is a later step that needs a provider.
/// </summary>
public sealed class OperationsService(
    IOnePondyDbContext db,
    SupportService support,
    FoodOrderAdminService orders,
    ReconciliationExceptionService reconciliation,
    IRateLimitStatistics rateLimits,
    IOptions<OperationsOptions> options,
    IOptions<NotificationOptions> notifications,
    TimeProvider time)
{
    public async Task<OperationsSnapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var settings = options.Value;

        var pendingReviews = db.Reviews.AsNoTracking().Where(r => r.Status == ReviewStatus.Pending);
        var reviews = new ReviewBacklog(
            await pendingReviews.CountAsync(cancellationToken),
            await pendingReviews.MinAsync(r => (DateTimeOffset?)r.CreatedAt, cancellationToken));

        var needsReply = db.SupportTickets.AsNoTracking().Where(t => t.Status == SupportTicketStatus.Open);
        var supportBacklog = new SupportBacklog(
            await support.CountsAsync(cancellationToken),
            await needsReply.MinAsync(t => (DateTimeOffset?)t.LastRequesterMessageAt, cancellationToken));

        var unsent = db.OutboxMessages.AsNoTracking().Where(m => m.DispatchedAt == null && m.Attempts < OnePondy.Domain.Payments.OutboxMessage.MaxAttempts);
        var sending = notifications.Value.Enabled;
        var notificationBacklog = new NotificationBacklog(
            sending,
            await unsent.CountAsync(cancellationToken),
            await unsent.MinAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken));

        var hourAgo = now.AddHours(-1);
        var dayAgo = now.AddHours(-24);
        var signIn = new SignInActivity(
            await db.OtpChallenges.AsNoTracking().CountAsync(c => c.CreatedAt >= hourAgo, cancellationToken),
            await db.OtpChallenges.AsNoTracking().CountAsync(c => c.CreatedAt >= dayAgo, cancellationToken));

        var attention = await orders.AttentionCountAsync(cancellationToken);
        var exceptions = await reconciliation.OpenCountAsync(cancellationToken);
        var limits = rateLimits.Snapshot();

        var alerts = Alerts(now, settings, reviews, supportBacklog, attention, exceptions, notificationBacklog, signIn, limits);
        return new OperationsSnapshot(now, alerts, reviews, supportBacklog, attention, exceptions, notificationBacklog, signIn, limits);
    }

    /// <summary>The alerts, most serious first. Pure: the same numbers always give the same alerts, which is what makes them testable.</summary>
    public static IReadOnlyList<OperationsAlert> Alerts(
        DateTimeOffset now,
        OperationsOptions settings,
        ReviewBacklog reviews,
        SupportBacklog support,
        int foodOrdersNeedingAttention,
        int openReconciliationExceptions,
        NotificationBacklog notifications,
        SignInActivity signIn,
        RateLimitSnapshot rateLimits)
    {
        var alerts = new List<OperationsAlert>();

        if (openReconciliationExceptions > 0)
        {
            alerts.Add(new OperationsAlert(
                "payment_exceptions",
                AlertSeverity.Critical,
                $"{openReconciliationExceptions} payment {Plural(openReconciliationExceptions, "difference needs", "differences need")} resolving: money may not match.",
                "payments"));
        }

        if (support.Counts.Escalated > 0)
        {
            alerts.Add(new OperationsAlert(
                "support_escalated",
                AlertSeverity.Critical,
                $"{support.Counts.Escalated} support {Plural(support.Counts.Escalated, "request has", "requests have")} been escalated.",
                "support"));
        }

        if (support.Counts.Overdue > 0)
        {
            alerts.Add(new OperationsAlert(
                "support_overdue",
                AlertSeverity.Warning,
                $"{support.Counts.Overdue} support {Plural(support.Counts.Overdue, "request has", "requests have")} missed a time target.",
                "support"));
        }

        if (foodOrdersNeedingAttention > 0)
        {
            alerts.Add(new OperationsAlert(
                "orders_attention",
                AlertSeverity.Warning,
                $"{foodOrdersNeedingAttention} food {Plural(foodOrdersNeedingAttention, "order looks", "orders look")} stuck.",
                "orders"));
        }

        if (reviews.OldestPendingAt is { } oldest && now - oldest > settings.ReviewBacklogWarnAfter)
        {
            alerts.Add(new OperationsAlert(
                "reviews_waiting",
                AlertSeverity.Warning,
                $"{reviews.Pending} {Plural(reviews.Pending, "review has", "reviews have")} been waiting for a moderator; the oldest for {Age(now - oldest)}.",
                "reviews"));
        }

        if (notifications.Sending
            && notifications.OldestUnsentAt is { } unsent
            && now - unsent > settings.NotificationBacklogWarnAfter)
        {
            alerts.Add(new OperationsAlert(
                "notifications_backlog",
                AlertSeverity.Warning,
                $"{notifications.Unsent} {Plural(notifications.Unsent, "notification has", "notifications have")} not been sent; the oldest has waited {Age(now - unsent)}.",
                "operations"));
        }

        if (signIn.CodesLastHour > settings.SignInCodesPerHourWarn)
        {
            alerts.Add(new OperationsAlert(
                "sign_in_abuse",
                AlertSeverity.Warning,
                $"{signIn.CodesLastHour} sign-in codes were requested in the last hour: this may be someone abusing sign-in.",
                "operations"));
        }

        foreach (var limited in rateLimits.Classes.Where(c => c.Rejected >= settings.RateLimitRefusalsWarn).OrderByDescending(c => c.Rejected))
        {
            alerts.Add(new OperationsAlert(
                $"rate_limit_{limited.Name}",
                AlertSeverity.Warning,
                $"{limited.Rejected} {limited.Name} requests have been refused for coming too fast since the API started.",
                "operations"));
        }

        return [.. alerts.OrderByDescending(a => a.Severity)];
    }

    private static string Plural(int count, string one, string many) => count == 1 ? one : many;

    private static string Age(TimeSpan span) =>
        span.TotalDays >= 2 ? $"{(int)span.TotalDays} days" : span.TotalHours >= 2 ? $"{(int)span.TotalHours} hours" : $"{Math.Max(1, (int)span.TotalMinutes)} minutes";
}
