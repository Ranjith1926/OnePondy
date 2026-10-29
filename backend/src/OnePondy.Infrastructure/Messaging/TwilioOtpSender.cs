using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;

namespace OnePondy.Infrastructure.Messaging;

/// <summary>Settings for Twilio SMS delivery, used for development and test servers (spec D19).</summary>
public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string? AccountSid { get; set; }

    public string? AuthToken { get; set; }

    /// <summary>A Twilio phone number in E.164 form. Ignored when <see cref="MessagingServiceSid"/> is set.</summary>
    public string? From { get; set; }

    public string? MessagingServiceSid { get; set; }

    public string BaseUrl { get; set; } = "https://api.twilio.com/2010-04-01";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid)
        && !string.IsNullOrWhiteSpace(AuthToken)
        && (!string.IsNullOrWhiteSpace(From) || !string.IsNullOrWhiteSpace(MessagingServiceSid));
}

/// <summary>
/// Sends our own one-time codes as plain SMS through Twilio's Messages API. Twilio Verify is not used
/// because it would generate and check its own codes, bypassing the limits and hashing in
/// <c>OtpChallengeService</c>. SMS only: there is no email provider yet, and an email destination fails
/// loudly instead of pretending to send. Message text and codes are never logged.
/// </summary>
public sealed class TwilioOtpSender(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioOtpSender> logger)
    : IOtpSender
{
    private readonly TwilioOptions _options = options.Value;

    public Task SendCodeAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken) =>
        SendAsync(
            channel,
            destination,
            $"Your OnePondy verification code is {code}. It is valid for 5 minutes. Do not share this code with anyone.",
            cancellationToken);

    public Task SendAccountDeletedAsync(OtpChannel channel, string destination, CancellationToken cancellationToken) =>
        SendAsync(
            channel,
            destination,
            "Your OnePondy account has been deleted. If this was not you, contact OnePondy support.",
            cancellationToken);

    private async Task SendAsync(OtpChannel channel, string destination, string body, CancellationToken cancellationToken)
    {
        if (channel != OtpChannel.Sms)
        {
            throw new OtpDeliveryException("Email delivery is not configured; only SMS codes can be sent.");
        }

        var form = new Dictionary<string, string> { ["To"] = destination, ["Body"] = body };
        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            form["MessagingServiceSid"] = _options.MessagingServiceSid;
        }
        else
        {
            form["From"] = _options.From!;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/Accounts/{_options.AccountSid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}")));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Twilio's error body names the destination and the reason, so log only the status.
                logger.LogWarning("Twilio rejected an SMS with status {StatusCode}", (int)response.StatusCode);
                throw new OtpDeliveryException("The SMS provider could not send the message.");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not reach Twilio to send an SMS");
            throw new OtpDeliveryException("The SMS provider could not be reached.", exception);
        }
    }
}
