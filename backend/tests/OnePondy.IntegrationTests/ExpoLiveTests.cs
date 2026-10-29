using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Notifications;
using OnePondy.Infrastructure.Notifications;

namespace OnePondy.IntegrationTests;

/// <summary>
/// Runs only when <c>ONEPONDY_EXPO_LIVE_CHECK=1</c>. Talks to Expo's real push service with a made-up token that belongs to no
/// device, so no notification is delivered to anyone; it only proves our request is accepted and our reading of the reply is right.
/// </summary>
public sealed class ExpoLiveFactAttribute : FactAttribute
{
    public const string Variable = "ONEPONDY_EXPO_LIVE_CHECK";

    public ExpoLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip = $"Set {Variable}=1 to call Expo's real push service with a made-up token (nothing is delivered).";
        }
    }
}

public class ExpoLiveTests
{
    private static ExpoPushSender Sender() =>
        new(new HttpClient(), Options.Create(new NotificationOptions { Enabled = true }), NullLogger<ExpoPushSender>.Instance);

    [ExpoLiveFact]
    public async Task Expo_accepts_our_request_and_refuses_a_token_that_belongs_to_no_device_with_an_error_we_understand()
    {
        var token = "ExponentPushToken[" + Guid.NewGuid().ToString("N")[..22] + "]";

        var result = await Sender().SendAsync([new PushMessage(token, "OnePondy", "Test: no device has this token.", "booking.confirmed", Guid.NewGuid())], default);

        Assert.True(result.IsSuccess, result.Error?.Code);
        var ticket = Assert.Single(result.Value);
        Assert.Null(ticket.Id);
        Assert.False(string.IsNullOrWhiteSpace(ticket.Error));
    }

    [ExpoLiveFact]
    public async Task Expo_answers_a_receipt_request_and_has_no_receipt_for_a_ticket_we_never_got()
    {
        var result = await Sender().GetReceiptsAsync([Guid.NewGuid().ToString()], default);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Empty(result.Value);
    }
}
