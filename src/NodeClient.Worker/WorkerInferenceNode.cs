using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace NodeClient.Worker;

/// <summary>
/// IInferenceNode adapter that proxies requests to a remote Orchestrator.NodeWorker
/// over HTTP. Implements the same contract as OllamaInferenceNode / CopilotInferenceNode
/// so the routing engine treats remote workers transparently.
/// </summary>
public sealed class WorkerInferenceNode : IInferenceNode
{
    private readonly IWorkerClient _client;
    private readonly WorkerProviderConfig _config;
    private readonly ILogger<WorkerInferenceNode> _logger;
    private NodeHealthStatus _health = new()
    {
        State = HealthState.Unavailable,
        LastChecked = DateTimeOffset.MinValue
    };

    public string NodeId { get; }
    public NodeProviderType Provider => NodeProviderType.Worker;
    public NodeHealthStatus Health => _health;

    public NodeCapabilities Capabilities { get; }

    public WorkerInferenceNode(
        string nodeId,
        WorkerProviderConfig config,
        IWorkerClient client,
        ILogger<WorkerInferenceNode> logger)
    {
        NodeId = nodeId;
        _config = config;
        _client = client;
        _logger = logger;
        Capabilities = new NodeCapabilities
        {
            NodeId = nodeId,
            Model = config.DefaultModel,
            VramMb = (int)config.GpuVramTotalMB,
            SupportsStreaming = false  // relay over HTTP — streaming not supported in v1
        };
    }

    public async Task<InferenceResult> ExecuteAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "WorkerNode {NodeId} relaying inference promptLen={Len}",
            NodeId, request.Prompt.Length);

        return await _client.ExecuteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Streaming not supported for Worker nodes in v1 — falls back to ExecuteAsync.
    /// </summary>
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
        try
        {
            var healthy = await _client.IsHealthyAsync(cancellationToken);
            sw.Stop();

            var models = healthy
                ? await _client.ListModelsAsync(cancellationToken)
                : Array.Empty<ModelInfo>();

            _health = new NodeHealthStatus
            {
                State = healthy ? HealthState.Healthy : HealthState.Unavailable,
                LastChecked = DateTimeOffset.UtcNow,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                AvailableModels = models.Select(m => m.ModelId).ToList(),
                VramTotalMB = _config.GpuVramTotalMB > 0 ? _config.GpuVramTotalMB : null
            };
        }
        catch (Exception ex)
        {
            _health = new NodeHealthStatus
            {
                State = HealthState.Unavailable,
                LastChecked = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message
            };
        }

        return _health;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default) =>
        await _client.ListModelsAsync(cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
