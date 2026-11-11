using System.Collections.Concurrent;
using System.Threading.Channels;

namespace RsvpDesk.Web.Conversations;

/// <summary>Ids of stored <c>WebhookEvents</c> waiting to be processed.</summary>
public sealed class InboundQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<long> Reader => _channel.Reader;

    public void Enqueue(long webhookEventId) => _channel.Writer.TryWrite(webhookEventId);
}

/// <summary>Lets a request wait (briefly) for the worker to finish an event it just queued.</summary>
public sealed class ProcessingNotifier
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource> _waiters = new();

    /// <summary>Register before enqueueing, so a fast worker cannot finish first unnoticed.</summary>
    public Task Register(long eventId) =>
        _waiters.GetOrAdd(eventId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public void Completed(long eventId)
    {
        if (_waiters.TryRemove(eventId, out var tcs))
        {
            tcs.TrySetResult();
        }
    }

    public async Task<bool> WaitAsync(long eventId, Task registration, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await registration.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            _waiters.TryRemove(eventId, out _);
            return false;
        }
    }
}
