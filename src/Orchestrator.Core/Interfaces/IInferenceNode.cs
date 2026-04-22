using System.Runtime.CompilerServices;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

public interface IInferenceNode : IAsyncDisposable
{
    string NodeId { get; }
    NodeProviderType Provider { get; }
    NodeHealthStatus Health { get; }

    /// <summary>Legacy capabilities — used by existing routing/scoring code.</summary>
    NodeCapabilities Capabilities { get; }

    Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<InferenceChunk> StreamAsync(
        InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>Probe node health and update the Health property.</summary>
    Task<NodeHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
}
