using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orchestrator.Core;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Models;

namespace NodeClient.Worker;

/// <summary>
/// IInferenceNode adapter that proxies requests to a remote Orchestrator.NodeWorker
/// over HTTP. Implements the same contract as OllamaInferenceNode / CopilotInferenceNode
/// so the routing engine treats remote workers transparently.
/// </summary>
public sealed class WorkerInferenceNode : InferenceNodeBase
{
    private readonly IWorkerClient _client;
    private readonly WorkerProviderConfig _config;
    private readonly ILogger<WorkerInferenceNode> _logger;

    public override string NodeId { get; }
    public override NodeProviderType Provider => NodeProviderType.Worker;

    public override NodeCapabilities Capabilities { get; }

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

    public override async Task<InferenceResult> ExecuteAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "WorkerNode {NodeId} relaying inference promptLen={Len}",
            NodeId, request.Prompt.Length);

        return await _client.ExecuteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Worker health check includes latency timing, model enumeration,
    /// and VRAM reporting — overrides the base implementation entirely.
    /// </summary>
    public override async Task<NodeHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
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

    protected override Task<bool> CheckHealthCoreAsync(CancellationToken cancellationToken)
        => _client.IsHealthyAsync(cancellationToken);

    public override async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default) =>
        await _client.ListModelsAsync(cancellationToken);
}
