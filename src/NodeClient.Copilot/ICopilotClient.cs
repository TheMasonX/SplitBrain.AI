using Orchestrator.Core.Models;

namespace NodeClient.Copilot;

public interface ICopilotClient
{
    Task<string> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
