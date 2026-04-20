using Orchestrator.Core.Enums;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

public interface IInferenceQueue
{
    /// <summary>Enqueue a request. Returns false if the queue is at capacity.</summary>
    bool TryEnqueue(InferenceQueueItem item);

    /// <summary>Dequeue the next item in priority order. Waits until one is available or cancellation fires.</summary>
    ValueTask<InferenceQueueItem> DequeueAsync(CancellationToken cancellationToken = default);

    int Count { get; }
}
