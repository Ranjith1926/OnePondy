using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Notifications;
using OnePondy.Infrastructure;
using OnePondy.Infrastructure.Notifications;

namespace OnePondy.UnitTests.Infrastructure;

public class ExpoPushSenderTests
{
    private sealed class RecordingHandler(Func<HttpRequestMessage, string, (HttpStatusCode Status, string Json)> respond) : HttpMessageHandler
    {
        public List<(Uri Uri, string Body, HttpRequestMessage Request)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.RequestUri!, body, request));
            var (status, json) = respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static ExpoPushSender Sender(HttpMessageHandler handler, string? accessToken = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new NotificationOptions { Enabled = true, Expo = new ExpoOptions { AccessToken = accessToken } }),
            NullLogger<ExpoPushSender>.Instance);

    private static PushMessage Message(int i = 0) =>
        new($"ExponentPushToken[token{i:D18}]", "OnePondy", "Your booking is confirmed.", "booking.confirmed", Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private static (HttpStatusCode, string) OkTickets(string _, string body)
    {
        var count = JsonDocument.Parse(body).RootElement.GetArrayLength();
        return (HttpStatusCode.OK, JsonSerializer.Serialize(new { data = Enumerable.Range(0, count).Select(i => new { status = "ok", id = $"ticket-{i}" }) }));
    }

    [Fact]
    public async Task Sends_to_the_expo_endpoint_with_only_the_type_and_booking_id_as_data()
    {
        var handler = new RecordingHandler((_, body) => OkTickets("", body));

        var result = await Sender(handler).SendAsync([Message()], default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new PushTicket("ticket-0", null), Assert.Single(result.Value));
        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://exp.host/--/api/v2/push/send", call.Uri.ToString());
        Assert.Equal(HttpMethod.Post, call.Request.Method);
        using var document = JsonDocument.Parse(call.Body);
        var sent = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("ExponentPushToken[token000000000000000000]", sent.GetProperty("to").GetString());
        Assert.Equal("OnePondy", sent.GetProperty("title").GetString());
        Assert.Equal("Your booking is confirmed.", sent.GetProperty("body").GetString());
        Assert.Equal("high", sent.GetProperty("priority").GetString());
        Assert.Equal("default", sent.GetProperty("channelId").GetString());
        var data = sent.GetProperty("data");
        Assert.Equal(["bookingId", "type"], data.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("booking.confirmed", data.GetProperty("type").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", data.GetProperty("bookingId").GetString());
    }

    [Fact]
    public async Task A_big_list_is_sent_a_hundred_at_a_time_and_the_tickets_come_back_in_order()
    {
        var handler = new RecordingHandler((_, body) => OkTickets("", body));

        var result = await Sender(handler).SendAsync([.. Enumerable.Range(0, 250).Select(Message)], default);

        Assert.Equal(250, result.Value!.Count);
        Assert.Equal([100, 100, 50], handler.Calls.Select(c => JsonDocument.Parse(c.Body).RootElement.GetArrayLength()));
    }

    [Fact]
    public async Task An_error_ticket_carries_expos_own_error_name()
    {
        var handler = new RecordingHandler((_, _) => (HttpStatusCode.OK, """{"data":[{"status":"ok","id":"t1"},{"status":"error","message":"not registered","details":{"error":"DeviceNotRegistered"}},{"status":"error","message":"plain message"}]}"""));

        var result = await Sender(handler).SendAsync([Message(0), Message(1), Message(2)], default);

        Assert.Equal(
            [new PushTicket("t1", null), new PushTicket(null, "DeviceNotRegistered"), new PushTicket(null, "plain message")],
            result.Value);
    }

    [Fact]
    public async Task The_access_token_is_sent_only_when_one_is_configured()
    {
        var without = new RecordingHandler((_, body) => OkTickets("", body));
        await Sender(without).SendAsync([Message()], default);
        Assert.Null(without.Calls.Single().Request.Headers.Authorization);

        var with = new RecordingHandler((_, body) => OkTickets("", body));
        await Sender(with, accessToken: "secret-access-token").SendAsync([Message()], default);
        var header = with.Calls.Single().Request.Headers.Authorization!;
        Assert.Equal(("Bearer", "secret-access-token"), (header.Scheme, header.Parameter));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_request_expo_refuses_is_could_not_send_so_the_dispatcher_backs_off(HttpStatusCode status)
    {
        var result = await Sender(new RecordingHandler((_, _) => (status, """{"errors":[{"code":"X","message":"no"}]}"""))).SendAsync([Message()], default);

        Assert.Equal("notifications_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task A_reply_that_is_not_the_expected_shape_is_not_trusted()
    {
        Assert.Equal("notifications_unavailable", (await Sender(new RecordingHandler((_, _) => (HttpStatusCode.OK, "<html>")))
            .SendAsync([Message()], default)).Error!.Code);
        Assert.Equal("notifications_unavailable", (await Sender(new RecordingHandler((_, _) => (HttpStatusCode.OK, """{"data":{"nope":1}}""")))
            .SendAsync([Message()], default)).Error!.Code);
    }

    [Fact]
    public async Task Receipts_are_asked_for_by_ticket_id_and_read_per_ticket()
    {
        var handler = new RecordingHandler((_, _) => (HttpStatusCode.OK,
            """{"data":{"t1":{"status":"ok"},"t2":{"status":"error","message":"gone","details":{"error":"DeviceNotRegistered"}}}}"""));

        var result = await Sender(handler).GetReceiptsAsync(["t1", "t2", "t3"], default);

        Assert.Equal("https://exp.host/--/api/v2/push/getReceipts", handler.Calls.Single().Uri.ToString());
        using var sent = JsonDocument.Parse(handler.Calls.Single().Body);
        Assert.Equal(["t1", "t2", "t3"], sent.RootElement.GetProperty("ids").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new PushReceipt(true, null), result.Value!["t1"]);
        Assert.Equal(new PushReceipt(false, "DeviceNotRegistered"), result.Value["t2"]);
        Assert.False(result.Value.ContainsKey("t3")); // no receipt yet: absent, not an error
    }

    [Fact]
    public async Task Receipts_are_fetched_a_thousand_ids_at_a_time()
    {
        var handler = new RecordingHandler((_, _) => (HttpStatusCode.OK, """{"data":{}}"""));

        await Sender(handler).GetReceiptsAsync([.. Enumerable.Range(0, 2_300).Select(i => $"t{i}")], default);

        Assert.Equal([1000, 1000, 300], handler.Calls.Select(c => JsonDocument.Parse(c.Body).RootElement.GetProperty("ids").GetArrayLength()));
    }

    [Fact]
    public async Task A_failed_receipt_request_is_could_not_find_out()
    {
        var result = await Sender(new RecordingHandler((_, _) => (HttpStatusCode.InternalServerError, "{}"))).GetReceiptsAsync(["t1"], default);

        Assert.Equal("notifications_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task The_unavailable_sender_says_so_and_sends_nothing()
    {
        var sender = new UnavailablePushSender();

        Assert.False(sender.IsAvailable);
        Assert.Equal("notifications_unavailable", (await sender.SendAsync([Message()], default)).Error!.Code);
        Assert.Equal("notifications_unavailable", (await sender.GetReceiptsAsync(["t"], default)).Error!.Code);
    }
}

public class PushSenderRegistrationTests
{
    private sealed class TestEnvironment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "OnePondy.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var all = new Dictionary<string, string?>
        {
            ["ConnectionStrings:OnePondy"] = "Host=localhost;Database=onepondy_test;Username=test",
            ["Auth:Jwt:SigningKey"] = Convert.ToBase64String(new byte[32]),
            ["Auth:HashingKey"] = Convert.ToBase64String(new byte[32]),
        };
        foreach (var (key, value) in settings)
        {
            all[key] = value;
        }

        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(all).Build(), new TestEnvironment("Production"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Notifications_are_off_unless_switched_on()
    {
        using var provider = Build();

        var sender = provider.GetRequiredService<IPushSender>();

        Assert.IsType<UnavailablePushSender>(sender);
        Assert.False(sender.IsAvailable);
    }

    [Fact]
    public void Switching_notifications_on_uses_expo()
    {
        using var provider = Build(("Notifications:Enabled", "true"));

        Assert.IsType<ExpoPushSender>(provider.GetRequiredService<IPushSender>());
    }

    [Fact]
    public void The_defaults_are_off_a_day_of_usefulness_and_a_fifteen_minute_receipt_wait()
    {
        var options = new NotificationOptions();

        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromHours(24), options.MessageLifetime);
        Assert.Equal(TimeSpan.FromMinutes(15), options.ReceiptDelay);
        Assert.Equal("https://exp.host", options.Expo.BaseUrl);
        Assert.Null(options.Expo.AccessToken);
    }
}
