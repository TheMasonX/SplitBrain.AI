using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

public interface IInferenceNode
{
    string NodeId { get; }
    NodeCapabilities Capabilities { get; }

    Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default);
    Task<NodeHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}
