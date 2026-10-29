using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;

namespace OnePondy.Infrastructure.Messaging;

/// <summary>
/// Sends and checks sign-in codes with Twilio Verify, which routes SMS to India with its own registered
/// templates, so it works before OnePondy's own DLT registration (spec D19). Twilio creates the code;
/// <c>OtpChallengeService</c> still applies OnePondy's expiry, attempt and send limits around it.
/// SMS only. Codes and phone numbers are never logged.
/// </summary>
public sealed class TwilioVerifyOtpSender(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioVerifyOtpSender> logger)
    : IOtpSender
{
    private readonly TwilioOptions _options = options.Value;

    public bool ProviderChecksCodes => true;

    public Task SendCodeAsync(OtpChannel channel, string destination, string code, OtpPurpose purpose, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Twilio Verify creates the codes itself.");

    /// <summary>Verify cannot send custom text, so the deletion notice is only logged.</summary>
    public Task SendAccountDeletedAsync(OtpChannel channel, string destination, CancellationToken cancellationToken)
    {
        logger.LogInformation("Account deletion confirmed; Twilio Verify cannot send a confirmation message");
        return Task.CompletedTask;
    }

    public async Task StartVerificationAsync(OtpChannel channel, string destination, CancellationToken cancellationToken)
    {
        EnsureSms(channel);
        using var response = await PostAsync(
            "Verifications",
            new Dictionary<string, string> { ["To"] = destination, ["Channel"] = "sms" },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Twilio Verify refused to start a verification with status {StatusCode}", (int)response.StatusCode);
            throw new OtpDeliveryException("The SMS provider could not send the message.");
        }
    }

    public async Task<bool> CheckVerificationAsync(OtpChannel channel, string destination, string code, CancellationToken cancellationToken)
    {
        EnsureSms(channel);
        using var response = await PostAsync(
            "VerificationCheck",
            new Dictionary<string, string> { ["To"] = destination, ["Code"] = code },
            cancellationToken);

        // Twilio answers 404 when no verification is pending (expired or already approved): just a wrong code to us.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Twilio Verify refused a check with status {StatusCode}", (int)response.StatusCode);
            throw new OtpDeliveryException("The SMS provider could not check the code.");
        }

        var result = await response.Content.ReadFromJsonAsync<CheckResponse>(cancellationToken);
        return string.Equals(result?.Status, "approved", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSms(OtpChannel channel)
    {
        if (channel != OtpChannel.Sms)
        {
            throw new OtpDeliveryException("Email delivery is not configured; only SMS codes can be sent.");
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string path, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.VerifyBaseUrl.TrimEnd('/')}/Services/{_options.VerifyServiceSid}/{path}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}")));

        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not reach Twilio Verify");
            throw new OtpDeliveryException("The SMS provider could not be reached.", exception);
        }
    }

    private sealed record CheckResponse([property: JsonPropertyName("status")] string? Status);
}
