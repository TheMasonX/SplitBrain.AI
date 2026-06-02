using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace NodeClient.LlamaCpp;

/// <summary>
/// IInferenceNode adapter for a llama.cpp server instance.
///
/// Target hardware: GTX 1080 8 GB (Pascal, sm_61) running llama-server.
/// Flash attention is NOT enabled — Pascal has known instability with --flash-attn.
/// MoE offloading (--n-cpu-moe, --no-mmap, --mlock, TurboQuant KV cache) is configured
/// at the server launch command level, not via per-request parameters.
///
/// Health state mapping:
///   "ok"           → Healthy     (route normally)
///   "loading model" → Degraded   (server starting; do not route until Healthy)
///   "error" / null → Unavailable (server failed or unreachable)
/// </summary>
public sealed class LlamaCppInferenceNode : IInferenceNode
{
    private readonly ILlamaCppClient _client;
    private readonly LlamaCppClientOptions _opts;
    private readonly ILogger<LlamaCppInferenceNode> _logger;

    private NodeHealthStatus _health = new()
    {
        State       = HealthState.Unavailable,
        LastChecked = DateTimeOffset.MinValue
    };

    // NodeId comes from LlamaCppClientOptions.NodeId (default "B").
    // Routing by node, not by model name — ModelLabel is for observability only.
    public string NodeId => _opts.NodeId;
    public NodeProviderType Provider => NodeProviderType.LlamaCpp;
    public NodeHealthStatus Health => _health;
    public NodeCapabilities Capabilities { get; }

    public LlamaCppInferenceNode(
        ILlamaCppClient client,
        IOptions<LlamaCppClientOptions> options,
        ILogger<LlamaCppInferenceNode> logger)
    {
        _client = client;
        _opts   = options.Value;
        _logger = logger;
        Capabilities = new NodeCapabilities
        {
            NodeId           = _opts.NodeId,
            Model            = _opts.ModelLabel,
            VramMb           = 8192,   // GTX 1080; override via subclass or future config field if needed
            SupportsStreaming = true    // llama.cpp SSE streaming fully supported (first node to implement)
        };
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────

    public async Task<InferenceResult> ExecuteAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "LlamaCpp Node {NodeId} executing model={Model} promptLen={Len}",
            NodeId, _opts.ModelLabel, request.Prompt.Length);

        var sw   = Stopwatch.StartNew();
        var text = await _client.ExecuteAsync(request, cancellationToken);
        sw.Stop();

        _logger.LogInformation(
            "LlamaCpp Node {NodeId} completed latencyMs={Latency} approxTokensOut={Tokens}",
            NodeId, sw.ElapsedMilliseconds, text.Length / 4);

        return new InferenceResult
        {
            Text      = text,
            NodeId    = NodeId,
            Model     = _opts.ModelLabel,
            LatencyMs = (int)sw.ElapsedMilliseconds
        };
    }

    // ── StreamAsync ───────────────────────────────────────────────────────────

    /// <summary>
    /// True SSE streaming — yields real token deltas as they arrive.
    /// LatencyMs on the final InferenceResult is TTLT (time-to-last-token),
    /// NOT TTFT (time-to-first-token). Do not compare directly against non-streaming
    /// node latencies which typically report TTFT.
    ///
    /// If the server crashes mid-stream, the stream completes with partial text
    /// and IsFinal=true. A warning is logged but no exception is thrown, preserving
    /// whatever tokens were received.
    /// </summary>
    public async IAsyncEnumerable<InferenceChunk> StreamAsync(
        InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "LlamaCpp Node {NodeId} streaming model={Model} promptLen={Len}",
            NodeId, _opts.ModelLabel, request.Prompt.Length);

        var sw = Stopwatch.StartNew();
        var sb = new StringBuilder();

        await foreach (var delta in _client.StreamChunksAsync(request, cancellationToken))
        {
            sb.Append(delta);
            yield return new InferenceChunk
            {
                Content = delta,
                IsFinal = false
            };
        }

        sw.Stop();
        var fullText = sb.ToString();

        _logger.LogInformation(
            "LlamaCpp Node {NodeId} stream complete latencyMs={Latency} approxTokensOut={Tokens}",
            NodeId, sw.ElapsedMilliseconds, fullText.Length / 4);

        // Final sentinel chunk — IsFinal=true, empty Content, FinalResult carries accumulated text.
        yield return new InferenceChunk
        {
            Content     = string.Empty,
            IsFinal     = true,
            FinalResult = new InferenceResult
            {
                Text      = fullText,
                NodeId    = NodeId,
                Model     = _opts.ModelLabel,
                LatencyMs = (int)sw.ElapsedMilliseconds
            }
        };
    }

    // ── GetHealthAsync ────────────────────────────────────────────────────────

    public async Task<NodeHealthStatus> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        NodeHealthStatus status;

        try
        {
            var serverStatus = await _client.GetServerStatusAsync(cancellationToken);
            sw.Stop();
            var models = (serverStatus == "ok")
                ? await _client.ListModelsAsync(cancellationToken)
                : Array.Empty<ModelInfo>();

            // Health state mapping (see class-level XML doc):
            //   "ok"           → Healthy
            //   "loading model" → Degraded (server is initialising; do not route)
            //   "error" / null → Unavailable
            var state = serverStatus switch
            {
                "ok"           => HealthState.Healthy,
                "loading model" => HealthState.Degraded,
                _               => HealthState.Unavailable
            };

            status = new NodeHealthStatus
            {
                State           = state,
                LastChecked     = DateTimeOffset.UtcNow,
                LatencyMs       = sw.Elapsed.TotalMilliseconds,
                AvailableModels = models.Select(m => m.ModelId).ToList(),
                ErrorMessage    = serverStatus is null ? "No response from /health" : null
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            status = new NodeHealthStatus
            {
                State        = HealthState.Unavailable,
                LastChecked  = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message
            };
        }

        _health = status;
        return status;
    }

    // ── ListModelsAsync ───────────────────────────────────────────────────────

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default) =>
        _client.ListModelsAsync(cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
