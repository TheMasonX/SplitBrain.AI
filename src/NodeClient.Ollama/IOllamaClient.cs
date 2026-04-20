using Orchestrator.Core.Models;

namespace NodeClient.Ollama;

public interface IOllamaClient
{
    Task<string> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
