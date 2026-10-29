using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;
using OnePondy.Infrastructure.Messaging;

namespace OnePondy.UnitTests.Infrastructure;

public class TwilioVerifyOtpSenderTests
{
    private sealed class StubHandler(HttpStatusCode status, string json = "{}") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static TwilioVerifyOtpSender Sender(StubHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new TwilioOptions { AccountSid = "ACtest", AuthToken = "token", VerifyServiceSid = "VAtest" }),
            NullLogger<TwilioVerifyOtpSender>.Instance);

    [Fact]
    public async Task Starts_a_verification_by_sms_on_the_verify_service()
    {
        var handler = new StubHandler(HttpStatusCode.Created);

        await Sender(handler).StartVerificationAsync(OtpChannel.Sms, "+919876543210", default);

        Assert.Equal("https://verify.twilio.com/v2/Services/VAtest/Verifications", handler.Request!.RequestUri!.ToString());
        Assert.Contains("To=%2B919876543210", handler.Body);
        Assert.Contains("Channel=sms", handler.Body);
    }

    [Fact]
    public async Task Approved_status_means_the_code_is_correct()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"status":"approved"}""");

        Assert.True(await Sender(handler).CheckVerificationAsync(OtpChannel.Sms, "+919876543210", "123456", default));
        Assert.EndsWith("/VerificationCheck", handler.Request!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Pending_status_or_no_pending_verification_means_the_code_is_wrong()
    {
        Assert.False(await Sender(new StubHandler(HttpStatusCode.OK, """{"status":"pending"}"""))
            .CheckVerificationAsync(OtpChannel.Sms, "+919876543210", "000000", default));
        Assert.False(await Sender(new StubHandler(HttpStatusCode.NotFound))
            .CheckVerificationAsync(OtpChannel.Sms, "+919876543210", "000000", default));
    }

    [Fact]
    public async Task A_provider_error_surfaces_as_a_delivery_failure()
    {
        await Assert.ThrowsAsync<OtpDeliveryException>(
            () => Sender(new StubHandler(HttpStatusCode.InternalServerError)).StartVerificationAsync(OtpChannel.Sms, "+919876543210", default));
        await Assert.ThrowsAsync<OtpDeliveryException>(
            () => Sender(new StubHandler(HttpStatusCode.InternalServerError)).CheckVerificationAsync(OtpChannel.Sms, "+919876543210", "1", default));
    }
}
