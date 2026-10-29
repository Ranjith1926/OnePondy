using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Messaging;

namespace OnePondy.UnitTests.Infrastructure;

public class TwilioOtpSenderTests
{
    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    private static TwilioOtpSender Sender(RecordingHandler handler, TwilioOptions? options = null) =>
        new(
            new HttpClient(handler),
            Options.Create(options ?? new TwilioOptions { AccountSid = "ACtest", AuthToken = "token", From = "+15005550006" }),
            NullLogger<TwilioOtpSender>.Instance);

    [Fact]
    public async Task Sends_the_code_as_an_authenticated_form_post_to_the_messages_endpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);

        await Sender(handler).SendCodeAsync(OtpChannel.Sms, "+919876543210", "123456", OtpPurpose.SignIn, default);

        Assert.Equal("https://api.twilio.com/2010-04-01/Accounts/ACtest/Messages.json", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Basic", handler.Request.Headers.Authorization!.Scheme);
        Assert.Contains("To=%2B919876543210", handler.Body);
        Assert.Contains("From=%2B15005550006", handler.Body);
        Assert.Contains("123456", handler.Body);
    }

    [Fact]
    public async Task A_messaging_service_replaces_the_from_number()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        var options = new TwilioOptions { AccountSid = "ACtest", AuthToken = "token", From = "+15005550006", MessagingServiceSid = "MG123" };

        await Sender(handler, options).SendCodeAsync(OtpChannel.Sms, "+919876543210", "123456", OtpPurpose.SignIn, default);

        Assert.Contains("MessagingServiceSid=MG123", handler.Body);
        Assert.DoesNotContain("From=", handler.Body);
    }

    [Fact]
    public async Task A_rejected_message_surfaces_as_a_delivery_failure()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<OtpDeliveryException>(
            () => Sender(handler).SendCodeAsync(OtpChannel.Sms, "+919876543210", "123456", OtpPurpose.SignIn, default));
    }

    [Fact]
    public async Task Email_destinations_are_refused_because_no_email_provider_exists()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);

        await Assert.ThrowsAsync<OtpDeliveryException>(
            () => Sender(handler).SendCodeAsync(OtpChannel.Email, "a@example.com", "123456", OtpPurpose.SignIn, default));
        Assert.Null(handler.Request);
    }
}
