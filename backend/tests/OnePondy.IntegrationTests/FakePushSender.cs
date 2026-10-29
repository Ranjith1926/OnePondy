using OnePondy.Application.Common;
using OnePondy.Application.Notifications;

namespace OnePondy.IntegrationTests;

/// <summary>A push service for HTTP tests: nothing leaves the process, and it is safe to use from several dispatchers at once.</summary>
public sealed class FakePushSender : IPushSender
{
    private readonly object _gate = new();
    private int _tickets;

    public bool IsAvailable => true;

    private readonly List<PushMessage> _sent = [];

    public IReadOnlyList<PushMessage> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    public Task<Result<IReadOnlyList<PushTicket>>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _sent.AddRange(messages);
            IReadOnlyList<PushTicket> tickets = [.. messages.Select(_ => new PushTicket($"ticket_it_{Interlocked.Increment(ref _tickets)}_{Guid.NewGuid():N}", null))];
            return Task.FromResult(Result<IReadOnlyList<PushTicket>>.Success(tickets));
        }
    }

    public Task<Result<IReadOnlyDictionary<string, PushReceipt>>> GetReceiptsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyDictionary<string, PushReceipt>>.Success(ticketIds.ToDictionary(id => id, _ => new PushReceipt(true, null))));
}
