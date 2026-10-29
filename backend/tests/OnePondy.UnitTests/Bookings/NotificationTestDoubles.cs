using OnePondy.Application.Common;
using OnePondy.Application.Notifications;

namespace OnePondy.UnitTests.Bookings;

/// <summary>A push service that records what it was asked to send and can be told to refuse or fail. Nothing leaves the process.</summary>
public sealed class FakePushSender : IPushSender
{
    private int _tickets;

    public bool IsAvailable { get; set; } = true;

    public List<PushMessage> Sent { get; } = [];

    /// <summary>The whole request fails, as if Expo were unreachable.</summary>
    public bool SendFails { get; set; }

    /// <summary>For these tokens Expo answers with an error ticket, for example <c>DeviceNotRegistered</c>.</summary>
    public Dictionary<string, string> TokenErrors { get; } = [];

    /// <summary>What Expo says about a ticket when asked for receipts. A ticket not in here has no receipt yet.</summary>
    public Dictionary<string, PushReceipt> Receipts { get; } = [];

    public bool ReceiptsFail { get; set; }

    public int ReceiptRequests { get; private set; }

    public List<string> TicketsIssued { get; } = [];

    public Task<Result<IReadOnlyList<PushTicket>>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        if (SendFails)
        {
            return Task.FromResult(Result<IReadOnlyList<PushTicket>>.Failure(NotificationErrors.Unavailable));
        }

        var tickets = new List<PushTicket>();
        foreach (var message in messages)
        {
            Sent.Add(message);
            if (TokenErrors.TryGetValue(message.To, out var error))
            {
                tickets.Add(new PushTicket(null, error));
                continue;
            }

            var id = $"ticket_{++_tickets}";
            TicketsIssued.Add(id);
            tickets.Add(new PushTicket(id, null));
        }

        return Task.FromResult(Result<IReadOnlyList<PushTicket>>.Success(tickets));
    }

    public Task<Result<IReadOnlyDictionary<string, PushReceipt>>> GetReceiptsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken)
    {
        ReceiptRequests++;
        if (ReceiptsFail)
        {
            return Task.FromResult(Result<IReadOnlyDictionary<string, PushReceipt>>.Failure(NotificationErrors.Unavailable));
        }

        IReadOnlyDictionary<string, PushReceipt> found = ticketIds.Where(Receipts.ContainsKey).ToDictionary(id => id, id => Receipts[id]);
        return Task.FromResult(Result<IReadOnlyDictionary<string, PushReceipt>>.Success(found));
    }
}
