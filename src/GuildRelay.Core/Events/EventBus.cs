using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GuildRelay.Core.Events;

/// <summary>
/// Bounded in-process queue of <see cref="DetectionEvent"/>. Full buffer
/// drops newly-published events rather than blocking the producer.
/// </summary>
public sealed class EventBus
{
    private readonly Channel<DetectionEvent> _channel;

    public EventBus(int capacity)
    {
        _channel = Channel.CreateBounded<DetectionEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Publishes an event to the bus. If the buffer is full, the event is
    /// silently dropped (DropWrite mode). This is fire-and-forget by design:
    /// features should not block or retry on a full bus.
    /// </summary>
    public ValueTask PublishAsync(DetectionEvent evt, CancellationToken ct)
    {
        _channel.Writer.TryWrite(evt);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<DetectionEvent> ConsumeAllAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var evt))
                yield return evt;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();
}
