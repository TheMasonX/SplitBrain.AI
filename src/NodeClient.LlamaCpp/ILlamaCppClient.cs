using Orchestrator.Core.Models;

namespace NodeClient.LlamaCpp;

/// <summary>HTTP client contract for a running llama.cpp server instance.</summary>
public interface ILlamaCppClient
{
    /// <summary>
    /// Send a prompt to POST /v1/chat/completions (non-streaming) and return the full response text.
    /// </summary>
    Task<string> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream tokens from POST /v1/chat/completions via OpenAI-compatible SSE.
    /// Yields text deltas as they arrive. Completes on [DONE] sentinel, finish_reason, or stream close.
    /// If the stream closes without [DONE] (server crash), enumeration completes silently
    /// with partial accumulated text — callers should treat this as a degraded result.
    /// </summary>
    IAsyncEnumerable<string> StreamChunksAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Probe GET /health. Returns true if the server is ready ("ok"),
    /// false for any non-ok status including "loading model" or "error".
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /health and return the raw status string.
    /// Known values: "ok", "loading model", "error". Null on network failure.
    /// </summary>
    Task<string?> GetServerStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /v1/models — returns list of model IDs the server reports (typically one).</summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
}
