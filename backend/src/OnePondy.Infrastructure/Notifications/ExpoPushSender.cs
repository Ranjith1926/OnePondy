using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.Notifications;

namespace OnePondy.Infrastructure.Notifications;

/// <summary>
/// Expo's push service (spec D7): <c>POST /--/api/v2/push/send</c> for messages and <c>POST /--/api/v2/push/getReceipts</c> for
/// whether they arrived. Limits from Expo's documentation: 100 messages per send and 1,000 receipt ids per request. Neither the
/// message text nor the tokens are ever logged, only status codes.
/// </summary>
public sealed class ExpoPushSender(HttpClient httpClient, IOptions<NotificationOptions> options, ILogger<ExpoPushSender> logger)
    : IPushSender
{
    public const int MaxMessagesPerRequest = 100;
    public const int MaxReceiptsPerRequest = 1000;

    private readonly ExpoOptions _options = options.Value.Expo;

    public bool IsAvailable => true;

    public async Task<Result<IReadOnlyList<PushTicket>>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        var tickets = new List<PushTicket>(messages.Count);
        foreach (var chunk in messages.Chunk(MaxMessagesPerRequest))
        {
            var body = chunk.Select(m => new
            {
                to = m.To,
                title = m.Title,
                body = m.Body,
                // Only the kind of notification and the booking it is about. The app opens the booking through the signed-in API.
                data = new { type = m.Type, bookingId = m.BookingId },
                sound = "default",
                priority = "high",
                channelId = "default",
                ttl = 86_400,
            });

            using var document = await PostAsync("push/send", body, cancellationToken);
            if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return NotificationErrors.Unavailable;
            }

            foreach (var ticket in data.EnumerateArray())
            {
                var status = ticket.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    tickets.Add(new PushTicket(ticket.TryGetProperty("id", out var id) ? id.GetString() : null, null));
                }
                else
                {
                    tickets.Add(new PushTicket(null, ErrorOf(ticket)));
                }
            }
        }

        return tickets;
    }

    public async Task<Result<IReadOnlyDictionary<string, PushReceipt>>> GetReceiptsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken)
    {
        var receipts = new Dictionary<string, PushReceipt>(StringComparer.Ordinal);
        foreach (var chunk in ticketIds.Chunk(MaxReceiptsPerRequest))
        {
            using var document = await PostAsync("push/getReceipts", new { ids = chunk }, cancellationToken);
            if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return NotificationErrors.Unavailable;
            }

            foreach (var entry in data.EnumerateObject())
            {
                var ok = entry.Value.TryGetProperty("status", out var s) && string.Equals(s.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
                receipts[entry.Name] = new PushReceipt(ok, ok ? null : ErrorOf(entry.Value));
            }
        }

        return receipts;
    }

    /// <summary>Expo's own error name when there is one (for example <c>DeviceNotRegistered</c>), otherwise its message.</summary>
    private static string ErrorOf(JsonElement element)
    {
        if (element.TryGetProperty("details", out var details)
            && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("error", out var error)
            && error.GetString() is { Length: > 0 } name)
        {
            return name;
        }

        return element.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } text ? text : "error";
    }

    private async Task<JsonDocument?> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/--/api/v2/{path}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 429 and 5xx are worth retrying and 4xx are not, but both reach the dispatcher as "could not send"; it backs off.
                logger.LogWarning("The Expo push service answered {StatusCode}", (int)response.StatusCode);
                return null;
            }

            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Could not reach the Expo push service");
            return null;
        }
    }
}

/// <summary>Used while notifications are off or Expo is not set up: nothing is sent, and the dispatcher leaves messages alone.</summary>
public sealed class UnavailablePushSender : IPushSender
{
    public bool IsAvailable => false;

    public Task<Result<IReadOnlyList<PushTicket>>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<PushTicket>>.Failure(NotificationErrors.Unavailable));

    public Task<Result<IReadOnlyDictionary<string, PushReceipt>>> GetReceiptsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyDictionary<string, PushReceipt>>.Failure(NotificationErrors.Unavailable));
}
