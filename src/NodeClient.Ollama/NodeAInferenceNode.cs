using Microsoft.Extensions.Logging;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NodeClient.Ollama;

public sealed class NodeAInferenceNode : IInferenceNode
{
    private const string Model = "qcoder:latest";

    private readonly IOllamaClient _client;
    private readonly ILogger<NodeAInferenceNode> _logger;
    private NodeHealthStatus _health = new() { State = HealthState.Unavailable, LastChecked = DateTimeOffset.MinValue };

    public string NodeId => "A";
    public NodeProviderType Provider => NodeProviderType.Ollama;
    public NodeHealthStatus Health => _health;

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

    public async IAsyncEnumerable<InferenceChunk> StreamAsync(
        InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(request, cancellationToken);
        yield return new InferenceChunk
        {
            Content = result.Text,
            IsFinal = true,
            FinalResult = new Orchestrator.Core.Models.InferenceResult
            {
                Text = result.Text,
                NodeId = result.NodeId,
                Model = result.Model,
                LatencyMs = result.LatencyMs
            }
        };
    }

    public async Task<NodeHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        NodeHealthStatus status;
        try
        {
            var isHealthy = await _client.IsHealthyAsync(cancellationToken);
            status = new NodeHealthStatus
            {
                State = isHealthy ? HealthState.Healthy : HealthState.Degraded,
                LastChecked = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            status = new NodeHealthStatus
            {
                State = HealthState.Unavailable,
                LastChecked = DateTimeOffset.UtcNow
            };
        }
        _health = status;
        return status;
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> result = [new ModelInfo { ModelId = Model }];
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

