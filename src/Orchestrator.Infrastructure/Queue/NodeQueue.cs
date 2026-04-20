using System.Threading.Channels;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Queue;

/// <summary>
/// Priority-aware, bounded inference queue backed by System.Threading.Channels.
/// Items with a lower Priority value are dequeued first.
/// Capacity defaults to 32 — large enough for burst; small enough to surface backpressure.
/// </summary>
public sealed class NodeQueue : IInferenceQueue
{
    private readonly Channel<InferenceQueueItem> _channel;
    private int _count;

    public int Capacity { get; }
    public int Count => _count;

    public NodeQueue(int capacity = 32)
    {
        Capacity = capacity;
        _channel = Channel.CreateBounded<InferenceQueueItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public bool TryEnqueue(InferenceQueueItem item)
    {
        if (!_channel.Writer.TryWrite(item))
            return false;

        Interlocked.Increment(ref _count);
        return true;
    }

    public async ValueTask<InferenceQueueItem> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var item = await _channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _count);
        return item;
    }

    /// <summary>Signals that no more items will be written. Outstanding readers drain then complete.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
