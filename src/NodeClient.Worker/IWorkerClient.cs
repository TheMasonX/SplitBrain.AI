using Orchestrator.Core.Models;

namespace NodeClient.Worker;

/// <summary>HTTP client contract for a remote Orchestrator.NodeWorker instance.</summary>
public interface IWorkerClient
{
    Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
}
