namespace OnePondy.Application.Identity;

public sealed class OtpOptions
{
    public const string SectionName = "Auth:Otp";

    public int CodeLength { get; set; } = 6;

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 5;

    /// <summary>Minimum wait before another code can be sent to the same destination.</summary>
    public TimeSpan ResendInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Codes allowed per destination within <see cref="DestinationWindow"/>.</summary>
    public int MaxPerDestination { get; set; } = 3;

    public TimeSpan DestinationWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Cap on codes sent per UTC day across all destinations, to limit SMS spend (spec §12).</summary>
    public int DailySendLimit { get; set; } = 1000;

    /// <summary>Country calling codes accepted for SMS; others use email (spec D1).</summary>
    public string[] SmsCallingCodes { get; set; } = ["91"];
}

public sealed class SessionOptions
{
    public const string SectionName = "Auth:Sessions";

    /// <summary>Refresh-token lifetime; each refresh extends the session by this much.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
}
