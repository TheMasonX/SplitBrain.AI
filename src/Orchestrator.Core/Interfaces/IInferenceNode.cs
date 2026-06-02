using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

public interface IInferenceNode : IAsyncDisposable
{
    string NodeId { get; }
    NodeProviderType Provider { get; }
    NodeHealthStatus Health { get; }
    NodeCapabilities Capabilities { get; }
    Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<InferenceChunk> StreamAsync(InferenceRequest request, CancellationToken cancellationToken = default);
    Task<NodeHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
}
