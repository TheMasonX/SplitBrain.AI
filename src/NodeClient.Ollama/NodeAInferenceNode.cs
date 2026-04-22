using Microsoft.Extensions.Logging;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Diagnostics;

namespace NodeClient.Ollama;

public sealed class NodeAInferenceNode : IInferenceNode
{
    private const string Model = "qcoder:latest";

    private readonly IOllamaClient _client;
    private readonly ILogger<NodeAInferenceNode> _logger;

    public string NodeId => "A";

    public NodeCapabilities Capabilities { get; } = new()
    {
        NodeId = "A",
        Model = Model,
        VramMb = 8192,
        SupportsStreaming = true
    };

    public NodeAInferenceNode(IOllamaClient client, ILogger<NodeAInferenceNode> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var req = request with { Model = Model };
        _logger.LogDebug("Node A executing model={Model} promptLen={Len}", Model, request.Prompt.Length);
        var sw = Stopwatch.StartNew();
        var text = await _client.ExecuteAsync(req, cancellationToken);
        sw.Stop();
        _logger.LogInformation("Node A completed latencyMs={Latency} tokensOut={Tokens}", sw.ElapsedMilliseconds, text.Length / 4);

        return new InferenceResult
        {
            Text = text,
            NodeId = NodeId,
            Model = Model,
            LatencyMs = (int)sw.ElapsedMilliseconds
        };
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
