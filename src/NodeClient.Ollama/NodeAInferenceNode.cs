using Microsoft.Extensions.Logging;
using Orchestrator.Core;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;
using System.Diagnostics;

namespace NodeClient.Ollama;

public sealed class NodeAInferenceNode : InferenceNodeBase
{
    private const string Model = "qcoder:latest";

    private readonly IOllamaClient _client;
    private readonly ILogger<NodeAInferenceNode> _logger;

    public override string NodeId => "A";
    public override NodeProviderType Provider => NodeProviderType.Ollama;

    public override NodeCapabilities Capabilities { get; } = new()
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

    public override async Task<InferenceResult> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
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

    protected override Task<bool> CheckHealthCoreAsync(CancellationToken cancellationToken)
        => _client.IsHealthyAsync(cancellationToken);

    public override Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> result = [new ModelInfo { ModelId = Model }];
        return Task.FromResult(result);
    }
}
