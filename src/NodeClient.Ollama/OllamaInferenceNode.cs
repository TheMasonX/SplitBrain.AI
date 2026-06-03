using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace NodeClient.Ollama;

/// <summary>
/// Config-driven Ollama inference node created dynamically from <see cref="NodeConfiguration"/>.
/// One instance per node entry in nodes.json; constructed by the InferenceNodeFactory.
/// </summary>
public sealed class OllamaInferenceNode : IInferenceNode
{
    private readonly IOllamaClient _client;
    private readonly OllamaProviderConfig _config;
    private readonly ILogger<OllamaInferenceNode> _logger;
    private NodeHealthStatus _health = new()
    {
        State = HealthState.Unavailable,
        LastChecked = DateTimeOffset.MinValue
    };

    public string NodeId { get; }
    public NodeProviderType Provider => NodeProviderType.Ollama;
    public NodeHealthStatus Health => _health;
    public NodeCapabilities Capabilities { get; }

    public OllamaInferenceNode(
        string nodeId,
        OllamaProviderConfig config,
        IOllamaClient client,
        ILogger<OllamaInferenceNode> logger)
    {
        NodeId = nodeId;
        _config = config;
        _client = client;
        _logger = logger;
        Capabilities = new NodeCapabilities
        {
            NodeId = nodeId,
            Model = string.Empty,   // populated on first ListModelsAsync
            VramMb = (int)config.GpuVramTotalMB,
            SupportsStreaming = true
        };
    }

    public async Task<InferenceResult> ExecuteAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "OllamaNode {NodeId} executing model={Model} promptLen={Len}",
            NodeId, request.Model, request.Prompt.Length);

        var sw = Stopwatch.StartNew();
        var text = await _client.ExecuteAsync(request, cancellationToken);
        sw.Stop();

        _logger.LogInformation(
            "OllamaNode {NodeId} completed latencyMs={Latency}",
            NodeId, sw.ElapsedMilliseconds);

        return new InferenceResult
        {
            Text = text,
            NodeId = NodeId,
            Model = request.Model,
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
            FinalResult = result
        };
    }

    public async Task<NodeHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        NodeHealthStatus status;
        try
        {
            var healthy = await _client.IsHealthyAsync(cancellationToken);
            sw.Stop();

            var state = !healthy ? HealthState.Unavailable
                : sw.Elapsed.TotalMilliseconds > _config.TimeoutSeconds * 500
                    ? HealthState.Degraded
                    : HealthState.Healthy;

            status = new NodeHealthStatus
            {
                State = state,
                LastChecked = DateTimeOffset.UtcNow,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                VramTotalMB = _config.GpuVramTotalMB
            };
        }
        catch (Exception ex)
        {
            status = new NodeHealthStatus
            {
                State = HealthState.Unavailable,
                LastChecked = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message
            };
        }
        _health = status;
        return status;
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        // The base IOllamaClient doesn't expose model listing — return empty.
        // Richer health data (available models, running models) requires OllamaSharp.
        IReadOnlyList<ModelInfo> result = [];
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
