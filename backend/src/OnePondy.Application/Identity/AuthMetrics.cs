using System.Diagnostics.Metrics;

namespace OnePondy.Application.Identity;

/// <summary>
/// Counters that monitoring alerts can watch (spec §12: alert on OTP abuse and SMS spend spikes).
/// Dashboards and alert rules are added with monitoring in Phase 10.
/// </summary>
public static class AuthMetrics
{
    public const string MeterName = "OnePondy.Auth";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> CodesSent =
        Meter.CreateCounter<long>("onepondy.auth.otp.sent", description: "One-time codes sent.");

    public static readonly Counter<long> CodeRequestsLimited = Meter.CreateCounter<long>(
        "onepondy.auth.otp.rate_limited",
        description: "Code requests rejected by per-destination limits.");

    public static readonly Counter<long> DailyLimitReached = Meter.CreateCounter<long>(
        "onepondy.auth.otp.daily_limit_reached",
        description: "Code requests rejected because the daily send cap was reached.");

    public static readonly Counter<long> RefreshTokenReuse = Meter.CreateCounter<long>(
        "onepondy.auth.refresh_token.reuse",
        description: "Rotated refresh tokens presented again; the session is revoked.");
}
