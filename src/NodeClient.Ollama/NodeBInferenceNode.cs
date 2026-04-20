using Microsoft.Extensions.Logging;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Diagnostics;

namespace NodeClient.Ollama;

/// <summary>
/// Node B — GTX 1080 8 GB (Pascal).
/// Primary: qwen2.5-coder:7b-instruct-q5_K_M
/// Fallback: deepseek-coder:6.7b-instruct-q4_K_M
/// Flash attention DISABLED (Pascal instability).
/// Single-parallel enforced via Ollama env vars.
/// </summary>
public sealed class NodeBInferenceNode : IInferenceNode
{
    private const string PrimaryModel  = "qwen2.5-coder:7b-instruct-q5_K_M";
    private const string FallbackModel = "deepseek-coder:6.7b-instruct-q4_K_M";

    private readonly IOllamaClient _client;
    private readonly ILogger<NodeBInferenceNode> _logger;

    public string NodeId => "B";

    public NodeCapabilities Capabilities { get; } = new()
    {
        NodeId = "B",
        Model = PrimaryModel,
        VramMb = 8192,
        SupportsStreaming = true
    };

    public NodeBInferenceNode(IOllamaClient client, ILogger<NodeBInferenceNode> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var model = request.UseFallback ? FallbackModel : PrimaryModel;
        var req = request with { Model = model };
        _logger.LogDebug("Node B executing model={Model} fallback={Fallback} promptLen={Len}",
            model, request.UseFallback, request.Prompt.Length);

        var sw = Stopwatch.StartNew();
        try
        {
            var text = await _client.ExecuteAsync(req, cancellationToken);
            sw.Stop();
            _logger.LogInformation("Node B completed latencyMs={Latency} tokensOut={Tokens}",
                sw.ElapsedMilliseconds, text.Length / 4);

            return new InferenceResult
            {
                Text      = text,
                NodeId    = NodeId,
                Model     = model,
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !request.UseFallback)
        {
            // §12: model crash → retry once with fallback model
            sw.Restart();
            _logger.LogWarning(ex,
                "Node B primary model {PrimaryModel} failed — retrying with fallback {FallbackModel}",
                PrimaryModel, FallbackModel);

            var fallbackReq = request with { Model = FallbackModel, UseFallback = true };
            var fallbackText = await _client.ExecuteAsync(fallbackReq, cancellationToken);
            sw.Stop();
            _logger.LogInformation("Node B fallback completed latencyMs={Latency}", sw.ElapsedMilliseconds);

            return new InferenceResult
            {
                Text      = fallbackText,
                NodeId    = NodeId,
                Model     = FallbackModel,
                LatencyMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<NodeHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        NodeStatus status;
        try
        {
            status = await _client.IsHealthyAsync(cancellationToken)
                ? NodeStatus.Healthy
                : NodeStatus.Degraded;
        }
        catch
        {
            status = NodeStatus.Unavailable;
        }

        return new NodeHealth
        {
            NodeId = NodeId,
            Status = status,
            CheckedAt = DateTimeOffset.UtcNow
        };
    }
}
