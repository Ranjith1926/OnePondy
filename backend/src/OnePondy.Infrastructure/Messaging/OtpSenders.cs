using Microsoft.Extensions.Logging;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;

namespace OnePondy.Infrastructure.Messaging;

/// <summary>
/// Development only: writes codes to the API log instead of sending them. It is registered only when
/// <c>Auth:Otp:Delivery</c> is <c>Log</c> in the Development or Testing environment.
/// </summary>
public sealed class LoggingOtpSender(ILogger<LoggingOtpSender> logger) : IOtpSender
{
    public Task SendCodeAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "DEVELOPMENT ONLY: {Purpose} code for {Channel} {Destination} is {Code}",
            purpose,
            channel,
            destination,
            code);
        return Task.CompletedTask;
    }

    public Task SendAccountDeletedAsync(OtpChannel channel, string destination, CancellationToken cancellationToken)
    {
        logger.LogWarning("DEVELOPMENT ONLY: account deletion confirmed for {Channel} {Destination}", channel, destination);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Used until an SMS and email provider is chosen (spec D19). Sign-in reports that codes cannot be
/// sent rather than pretending to work.
/// </summary>
public sealed class UnavailableOtpSender : IOtpSender
{
    private const string Reason = "No SMS or email provider is configured for one-time codes (spec D19).";

    public Task SendCodeAsync(
        OtpChannel channel,
        string destination,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken) => throw new OtpDeliveryException(Reason);

    public Task SendAccountDeletedAsync(OtpChannel channel, string destination, CancellationToken cancellationToken) =>
        throw new OtpDeliveryException(Reason);
}
